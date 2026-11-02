using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CleanTemplate.Host.Health;

public sealed class RedisHealthCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await cache.GetAsync("__health_probe__", cancellationToken).ConfigureAwait(false);
        return HealthCheckResult.Healthy();
    }
}
