using CleanTemplate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Security;

public sealed class RefreshTokenCleanupBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RefreshTokenCleanupBackgroundService> _logger;
    private readonly RefreshTokenCleanupOptions _options;

    public RefreshTokenCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<RefreshTokenCleanupOptions> options,
        ILogger<RefreshTokenCleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Refresh token cleanup background service is disabled.");
            return;
        }

        var intervalMinutes = Math.Clamp(_options.IntervalMinutes, 1, 1440);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

        await RunCleanupAsync(stoppingToken).ConfigureAwait(false);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunCleanupAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var retentionDays = Math.Clamp(_options.RevokedRetentionDays, 0, 365);
            var revokedThreshold = nowUtc.AddDays(-retentionDays);

            var deletedRows = await dbContext.Set<RefreshToken>()
                .Where(token => token.ExpiresAtUtc <= nowUtc
                    || (token.RevokedAtUtc.HasValue && token.RevokedAtUtc <= revokedThreshold))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deletedRows > 0)
            {
                _logger.LogInformation(
                    "Refresh token cleanup removed {DeletedRows} tokens.",
                    deletedRows);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Refresh token cleanup failed.");
        }
    }
}
