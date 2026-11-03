using System.Diagnostics;
using System.Diagnostics.Metrics;
using CleanTemplate.Infrastructure.Extensions;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.Infrastructure.Security;
using CleanTemplate.Infrastructure.Tests.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Tests.Security;

[CollectionDefinition("Privilege audit", DisableParallelization = true)]
public sealed class PrivilegeAuditCollection;

[Collection("Privilege audit")]
public sealed class PrivilegeAuditTests
{
    [Fact]
    public async Task Autocommit_ManualAndExternal_LogAndMeasureCommittedChanges()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var activity = new Activity("audit-test").Start();
        activity.AddBaggage("actor", "untrusted-baggage-canary");
        var manual = new IdentityUserRole<Guid> { UserId = fixture.User, RoleId = fixture.Role };
        fixture.Db.Add(manual);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.Remove(manual);
        fixture.Db.SaveChanges();
        var external = fixture.Grant();
        fixture.Db.Add(external);
        await fixture.Db.SaveChangesAsync();
        external.SetExpiration(DateTime.UtcNow.AddHours(2));
        await fixture.Db.SaveChangesAsync();
        fixture.Db.Remove(external);
        await fixture.Db.SaveChangesAsync();

        Assert.Empty(await fixture.Db.UserRoles.ToListAsync());
        Assert.Empty(await fixture.Db.Set<ExternalRoleGrant>().ToListAsync());
        Assert.Equal(new[] { "manual:grant", "manual:revoke", "external:grant", "external:renew", "external:revoke" }, fixture.Changes);
        Assert.Equal(fixture.Changes, fixture.Measurements);
        Assert.All(fixture.Messages, message => { Assert.DoesNotContain("canary@example.test", message); Assert.DoesNotContain("secret-subject-canary", message); Assert.DoesNotContain("untrusted-baggage-canary", message); Assert.Contains("unknown", message); Assert.Contains("committed", message); Assert.Contains(fixture.User.ToString(), message); Assert.Contains(fixture.Role.ToString(), message); Assert.Contains(activity.TraceId.ToString(), message); Assert.Contains(activity.SpanId.ToString(), message); });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitTransaction_MultipleFlushes_OnlyEmitsAfterCommit(bool asyncCommit)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var tx = await fixture.Db.Database.BeginTransactionAsync();
        fixture.Db.Add(new IdentityUserRole<Guid> { UserId = fixture.User, RoleId = fixture.Role });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.Add(fixture.Grant());
        await fixture.Db.SaveChangesAsync();
        Assert.Empty(fixture.Changes);
        Assert.Empty(fixture.Measurements);
        if (asyncCommit) await tx.CommitAsync(); else tx.Commit();
        Assert.Equal(new[] { "manual:grant", "external:grant" }, fixture.Changes);
        Assert.Equal(fixture.Changes, fixture.Measurements);
        Assert.Equal(1, await fixture.Db.UserRoles.CountAsync());
        Assert.Equal(1, await fixture.Db.Set<ExternalRoleGrant>().CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rollback_DiscardsEventsAndData(bool asyncRollback)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var tx = await fixture.Db.Database.BeginTransactionAsync();
        fixture.Db.Add(fixture.Grant());
        await fixture.Db.SaveChangesAsync();
        if (asyncRollback) await tx.RollbackAsync(); else tx.Rollback();
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.Set<ExternalRoleGrant>().ToListAsync());
        Assert.Empty(fixture.Changes);
        Assert.Empty(fixture.Measurements);
    }

    [Fact]
    public async Task NetZeroAndFailedSave_DoNotEmit()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var tx = await fixture.Db.Database.BeginTransactionAsync())
        {
            var grant = fixture.Grant();
            fixture.Db.Add(grant);
            await fixture.Db.SaveChangesAsync();
            fixture.Db.Remove(grant);
            await fixture.Db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        fixture.Db.Add(new IdentityUserRole<Guid> { UserId = Guid.NewGuid(), RoleId = fixture.Role });
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.UserRoles.ToListAsync());
        Assert.Empty(await fixture.Db.Set<ExternalRoleGrant>().ToListAsync());
        Assert.Empty(fixture.Changes);
        Assert.Empty(fixture.Measurements);
    }

    [Fact]
    public async Task FailedFlush_PreservesEarlierSuccessfulFlush()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var tx = await fixture.Db.Database.BeginTransactionAsync();
        fixture.Db.Add(fixture.Grant());
        await fixture.Db.SaveChangesAsync();
        fixture.Db.Add(new IdentityUserRole<Guid> { UserId = Guid.NewGuid(), RoleId = fixture.Role });
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(fixture.Changes);
        await tx.CommitAsync();
        Assert.Equal(new[] { "external:grant" }, fixture.Changes);
        Assert.Equal(fixture.Changes, fixture.Measurements);
        Assert.Single(await fixture.Db.Set<ExternalRoleGrant>().ToListAsync());
    }

    [Fact]
    public async Task DisposedTransactionAndSavepointRollback_DoNotEmitFalseEvents()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (await fixture.Db.Database.BeginTransactionAsync())
        {
            fixture.Db.Add(fixture.Grant());
            await fixture.Db.SaveChangesAsync();
        }
        fixture.Db.ChangeTracker.Clear();
        await using (var tx = await fixture.Db.Database.BeginTransactionAsync())
        {
            await tx.CreateSavepointAsync("before");
            fixture.Db.Add(fixture.Grant());
            await fixture.Db.SaveChangesAsync();
            await tx.RollbackToSavepointAsync("before");
            await tx.CommitAsync();
        }
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.Set<ExternalRoleGrant>().ToListAsync());
        Assert.Empty(fixture.Changes);
        Assert.Empty(fixture.Measurements);
    }

    [Fact]
    public async Task RefreshReplay_IsMeasuredButOrdinaryExpiryIsNot()
    {
        var records = new List<(string Name, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == "CleanTemplate.Security") l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => records.Add((instrument.Name, tags.ToArray())));
        listener.Start();
        await using var fixture = await RefreshTokenDatabaseFixture.CreateAsync();
        await using var scope = fixture.First.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new RefreshTokenService(db, TimeProvider.System, Options.Create(new RefreshTokenOptions()));
        var user = Guid.NewGuid();
        var expired = await service.IssueAsync(user, "expiry", null);
        await db.Set<RefreshToken>().ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Null(await service.RotateAsync(expired.RefreshToken, user, "expired", null));
        Assert.DoesNotContain(records, r => r.Name == "security.refresh.replay");
        var issued = await service.IssueAsync(user, "original", null);
        Assert.NotNull(await service.RotateAsync(issued.RefreshToken, user, "replacement", null));
        Assert.Null(await service.RotateAsync(issued.RefreshToken, user, "replay", null));
        var replay = Assert.Single(records, r => r.Name == "security.refresh.replay");
        Assert.Empty(replay.Tags);
    }

    [Fact]
    public async Task ContextsInSameScope_DoNotOverwriteTransactionAudit()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var second = fixture.CreateSecondContext();
        await using var tx = await fixture.Db.Database.BeginTransactionAsync();
        fixture.Db.Add(fixture.Grant());
        await fixture.Db.SaveChangesAsync();
        await second.SaveChangesAsync();
        Assert.Empty(fixture.Changes);
        await tx.CommitAsync();
        Assert.Equal(new[] { "external:grant" }, fixture.Changes);
        Assert.Equal(fixture.Changes, fixture.Measurements);
        Assert.All(fixture.MeasurementTags, tags => Assert.Equal(new[] { "change", "source" }, tags.Order().ToArray()));
    }

    [Fact]
    public async Task ReplacingDifferentExternalLogin_RecordsBothAssignments()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = fixture.Grant();
        fixture.Db.Add(original);
        await fixture.Db.SaveChangesAsync();
        fixture.Changes.Clear();
        fixture.Measurements.Clear();
        await using var tx = await fixture.Db.Database.BeginTransactionAsync();
        fixture.Db.Remove(original);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.Add(new ExternalRoleGrant(fixture.User, "other-provider", "other-secret-canary", fixture.Role, original.ValidUntilUtc));
        await fixture.Db.SaveChangesAsync();
        await tx.CommitAsync();
        Assert.Equal(new[] { "external:revoke", "external:grant" }, fixture.Changes);
        Assert.Equal(fixture.Changes, fixture.Measurements);
        Assert.All(fixture.Messages, message => Assert.DoesNotContain("other-secret-canary", message));
    }

    [Fact]
    public async Task RestoringExpirationAcrossFlushesOrRecreation_IsNetZero()
    {
        await using var fixture = await Fixture.CreateAsync();
        var grant = fixture.Grant();
        fixture.Db.Add(grant);
        await fixture.Db.SaveChangesAsync();
        fixture.Changes.Clear();
        fixture.Measurements.Clear();
        var original = grant.ValidUntilUtc;
        await using var tx = await fixture.Db.Database.BeginTransactionAsync();
        grant.SetExpiration(original.AddHours(1));
        await fixture.Db.SaveChangesAsync();
        grant.SetExpiration(original);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.Remove(grant);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.Add(new ExternalRoleGrant(fixture.User, "provider", "secret-subject-canary", fixture.Role, original));
        await fixture.Db.SaveChangesAsync();
        await tx.CommitAsync();
        Assert.Empty(fixture.Changes);
        Assert.Empty(fixture.Measurements);
    }

    [Fact]
    public async Task FailingSinks_DoNotChangeCommittedData()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.FailSinks = true;
        await using var tx = await fixture.Db.Database.BeginTransactionAsync();
        fixture.Db.Add(fixture.Grant());
        await fixture.Db.SaveChangesAsync();
        await tx.CommitAsync();
        fixture.Db.ChangeTracker.Clear();
        Assert.Single(await fixture.Db.Set<ExternalRoleGrant>().ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        private readonly MeterListener _listener;
        private readonly CaptureLogger _logger;
        public ApplicationDbContext Db => _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        public Guid User { get; } = Guid.NewGuid();
        public Guid Role { get; } = Guid.NewGuid();
        public List<string> Changes => _logger.Changes;
        public List<string> Messages => _logger.Messages;
        public List<string> Measurements { get; } = [];
        public List<string[]> MeasurementTags { get; } = [];
        public bool FailSinks { get => _logger.Fail; set => _logger.Fail = value; }

        private Fixture()
        {
            _logger = new CaptureLogger();
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddProvider(_logger));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "SqliteInMemory",
                ["ConnectionStrings:DefaultConnection"] = $"Data Source=audit-{Guid.NewGuid():N};Mode=Memory;Cache=Shared"
            }).Build();
            services.AddInfrastructure(config);
            _provider = services.BuildServiceProvider();
            _scope = _provider.CreateAsyncScope();
            _listener = new MeterListener();
            _listener.InstrumentPublished = (instrument, listener) => { if (instrument.Meter.Name == "CleanTemplate.Security" && instrument.Name == "security.privilege.committed") listener.EnableMeasurementEvents(instrument); };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                if (FailSinks) throw new InvalidOperationException("listener failure");
                string? source = null, change = null;
                foreach (var tag in tags) { if (tag.Key == "source") source = tag.Value?.ToString(); if (tag.Key == "change") change = tag.Value?.ToString(); }
                MeasurementTags.Add(tags.ToArray().Select(t => t.Key).ToArray());
                Measurements.Add($"{source}:{change}");
            });
            _listener.Start();
        }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture.Db.Database.EnsureCreatedAsync();
            fixture.Db.Add(new ApplicationUser { Id = fixture.User, UserName = "user", Email = "canary@example.test" });
            fixture.Db.Add(new IdentityRole<Guid> { Id = fixture.Role, Name = "Admin" });
            await fixture.Db.SaveChangesAsync();
            return fixture;
        }

        public ExternalRoleGrant Grant() => new(User, "provider", "secret-subject-canary", Role, DateTime.UtcNow.AddHours(1));
        public ApplicationDbContext CreateSecondContext() => new(_scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        public async ValueTask DisposeAsync() { _listener.Dispose(); await _scope.DisposeAsync(); await _provider.DisposeAsync(); }
    }

    private sealed class CaptureLogger : ILoggerProvider, ILogger
    {
        public bool Fail { get; set; }
        public List<string> Changes { get; } = [];
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Name != "PrivilegeAssignmentCommitted") return;
            if (Fail) throw new InvalidOperationException("logger failure");
            var properties = ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(x => x.Key, x => x.Value);
            Changes.Add($"{properties["Source"]}:{properties["Change"]}");
            Messages.Add(formatter(state, exception));
        }
    }
}
