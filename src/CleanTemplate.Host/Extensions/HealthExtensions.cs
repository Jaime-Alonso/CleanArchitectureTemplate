using Asp.Versioning;
using CleanTemplate.Host.Health;
using CleanTemplate.Infrastructure.Caching.Options;
using CleanTemplate.Infrastructure.Database;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CleanTemplate.Host.Extensions;

public static class HealthExtensions
{
    public static IServiceCollection AddOperationalHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var timeoutText = configuration["HealthChecks:TimeoutSeconds"];
        if (timeoutText is not null && (!int.TryParse(timeoutText, out var value) || value is < 1 or > 60))
            throw new InvalidOperationException("HealthChecks:TimeoutSeconds must be an integer between 1 and 60.");
        var seconds = timeoutText is null ? 5 : int.Parse(timeoutText);
        var timeout = TimeSpan.FromSeconds(seconds);
        var provider = DatabaseProviderParser.Parse(configuration["DatabaseProvider"]);
        var primary = configuration.GetConnectionString("DefaultConnection")
            ?? (provider == DatabaseProvider.SqliteInMemory ? "Data Source=CleanTemplateInMemory;Mode=Memory;Cache=Shared" : throw new InvalidOperationException("Connection string 'DefaultConnection' not found."));
        var checks = services.AddHealthChecks();
        checks.Add(new HealthCheckRegistration("database-primary", _ => new DatabaseHealthCheck(new ReadDbConnectionFactory(primary, provider), seconds), HealthStatus.Unhealthy, ["ready"], timeout));
        if (string.Equals(configuration["ProductReads:Mode"], "Replica", StringComparison.OrdinalIgnoreCase))
        {
            var replica = configuration.GetConnectionString("ProductReadReplica") ?? throw new InvalidOperationException("ProductReadReplica is required.");
            checks.Add(new HealthCheckRegistration("database-replica", _ => new DatabaseHealthCheck(new ReadDbConnectionFactory(replica, provider), seconds), HealthStatus.Unhealthy, ["ready"], timeout));
        }
        var cache = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>() ?? new CacheOptions();
        if (cache.Provider == CacheProvider.Redis)
            checks.Add(new HealthCheckRegistration("redis", sp => new RedisHealthCheck(sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>()), HealthStatus.Unhealthy, ["ready"], timeout));
        return services;
    }

    public static void MapOperationalHealthChecks(this WebApplication app)
    {
        Map(app, "/health/live", _ => false);
        Map(app, "/health/ready", check => check.Tags.Contains("ready"));
        Map(app, "/health", check => check.Tags.Contains("ready"));
    }

    private static void Map(WebApplication app, string path, Func<HealthCheckRegistration, bool> predicate)
    {
        app.MapHealthChecks(path, new HealthCheckOptions
        {
            Predicate = predicate,
            AllowCachingResponses = false,
            ResultStatusCodes = { [HealthStatus.Healthy] = 200, [HealthStatus.Degraded] = 503, [HealthStatus.Unhealthy] = 503 },
            ResponseWriter = (context, report) => context.Response.WriteAsync(report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy")
        }).AllowAnonymous().DisableRateLimiting().ExcludeFromDescription().WithMetadata(new ApiVersionNeutralAttribute());
    }
}
