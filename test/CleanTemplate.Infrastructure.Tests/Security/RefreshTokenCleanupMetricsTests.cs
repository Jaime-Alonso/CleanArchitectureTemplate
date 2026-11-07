using System.Data.Common;
using System.Diagnostics.Metrics;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Tests.Security;

[Collection("Privilege audit")]
public sealed class RefreshTokenCleanupMetricsTests
{
    [Fact]
    public async Task UnavailableDatabase_RecordsFailureWithoutLoggingSensitiveValues()
    {
        var records = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = Listen(records, observed);
        var path = Path.Combine(Path.GetTempPath(), $"CANARY-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString()));
        await using var provider = services.BuildServiceProvider();
        var logger = new CleanupLogger();
        using var service = new RefreshTokenCleanupBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, Options.Create(new RefreshTokenCleanupOptions()), logger);
        try
        {
            await service.StartAsync(default);
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { await service.StopAsync(CancellationToken.None); }
        Assert.Single(records);
        Assert.True(records[0].Value > 0);
        Assert.Empty(records[0].Tags);
        Assert.DoesNotContain("CANARY", logger.Message);
        Assert.DoesNotContain(path, logger.Message);
        Assert.Contains("failed", logger.Message);
    }

    [Fact]
    public async Task StopDuringTimerWait_DoesNotRecordFailure()
    {
        var records = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = Listen(records, observed);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new CleanupCompletedInterceptor();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection).AddInterceptors(interceptor));
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
        using var service = new RefreshTokenCleanupBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, Options.Create(new RefreshTokenCleanupOptions()), new CleanupLogger());
        try
        {
            await service.StartAsync(default);
            await interceptor.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { await service.StopAsync(CancellationToken.None); }
        Assert.Empty(records);
    }

    [Fact]
    [Trait("Category", "ShutdownCleanupIsolated")]
    public async Task StopDuringBlockedCommand_AwaitsCancellationBeforeDisposingContext_WithoutWriting()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new BlockingCleanupInterceptor();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection).AddInterceptors(interceptor));
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF");
            var now = DateTime.UtcNow;
            db.Set<RefreshTokenFamily>().Add(RefreshTokenFamily.Create(Guid.NewGuid(), now.AddDays(-2), now.AddDays(-1), null));
            await db.SaveChangesAsync();
        }
        interceptor.Armed = true;
        using var service = new RefreshTokenCleanupBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, Options.Create(new RefreshTokenCleanupOptions()), new CleanupLogger());
        try
        {
            await service.StartAsync(default);
            await interceptor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var stop = service.StopAsync(CancellationToken.None);
            await interceptor.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(stop.IsCompleted);
            Assert.NotNull(interceptor.Context!.Database.ProviderName);
            interceptor.Release.TrySetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Throws<ObjectDisposedException>(() => interceptor.Context!.Database.ProviderName);
            await using var scope = provider.CreateAsyncScope();
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<RefreshTokenFamily>().CountAsync());
        }
        finally
        {
            interceptor.Release.TrySetResult();
            await service.StopAsync(CancellationToken.None);
        }
    }

    private sealed class BlockingCleanupInterceptor : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public DbContext? Context { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
            {
                Context = eventData.Context;
                Entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    Canceled.TrySetResult();
                    await Release.Task;
                    Assert.NotNull(eventData.Context);
                    _ = eventData.Context.Database.ProviderName;
                    throw;
                }
            }
            return result;
        }
    }

    private static MeterListener Listen(List<(long Value, KeyValuePair<string, object?>[] Tags)> records, TaskCompletionSource observed)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == "CleanTemplate.Security" && instrument.Name == "security.cleanup.failure") l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) => { records.Add((value, tags.ToArray())); observed.TrySetResult(); });
        listener.Start();
        return listener;
    }

    private sealed class CleanupCompletedInterceptor : DbCommandInterceptor
    {
        private int _deletes;
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) && Interlocked.Increment(ref _deletes) == 2) Completed.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CleanupLogger : ILogger<RefreshTokenCleanupBackgroundService>
    {
        public string Message { get; private set; } = "";
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Message = formatter(state, exception);
    }
}
