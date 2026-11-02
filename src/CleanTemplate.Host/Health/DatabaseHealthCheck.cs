using CleanTemplate.Infrastructure.Database;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CleanTemplate.Host.Health;

public sealed class DatabaseHealthCheck(ReadDbConnectionFactory factory, int timeoutSeconds) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        command.CommandTimeout = timeoutSeconds;
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return HealthCheckResult.Healthy();
    }
}
