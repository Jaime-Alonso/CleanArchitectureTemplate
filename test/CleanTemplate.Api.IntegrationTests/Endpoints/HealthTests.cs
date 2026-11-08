using System.Diagnostics;
using System.Net;
using CleanTemplate.Api.IntegrationTests.Infrastructure;
using CleanTemplate.Host.Extensions;
using CleanTemplate.Host.Health;
using CleanTemplate.Infrastructure.Database;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Api.IntegrationTests.Endpoints;

[Collection(HostIntegrationCollection.Name)]
public sealed class HealthTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task SaturatedGlobalLimiter_DoesNotLimitProbes()
    {
        using var host = new TestWebApplicationFactory("Testing", new Dictionary<string, string?>
        {
            ["RateLimiting:Global:PermitLimit"] = "1",
            ["RateLimiting:Global:WindowSeconds"] = "300"
        });
        using var client = host.CreateClient();
        using var first = await client.GetAsync("/api/products/");
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        using var rejected = await client.GetAsync("/api/products/");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        foreach (var path in new[] { "/health/live", "/health/ready", "/health" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task CooperativeTimeout_CancelsAndFinishesBeforeReadinessReturns()
    {
        var check = new BlockingCheck();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration("slow", _ => check,
                    HealthStatus.Unhealthy, ["ready"], TimeSpan.FromMilliseconds(150)));
            })));
        using var client = host.CreateClient();
        var elapsed = Stopwatch.StartNew();
        using var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10));
        Assert.True(check.Cancelled);
        Assert.True(check.Finished);
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());
    }

    private sealed class BlockingCheck : IHealthCheck
    {
        public bool Cancelled { get; private set; }
        public bool Finished { get; private set; }
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { Cancelled = true; throw; }
            finally { Finished = true; }
            return HealthCheckResult.Healthy();
        }
    }

    [Theory]
    [InlineData("Replica", "Memory", true, false)]
    [InlineData("Primary", "Redis", false, true)]
    [InlineData("Primary", "Memory", false, false)]
    public async Task OperationalRegistrations_ExecuteConfiguredDependencies(string mode, string cache, bool replicaFailure, bool redisFailure)
    {
        var probeCache = new ProbeCache { Fail = redisFailure };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseProvider"] = "SqliteInMemory",
            ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
            ["ConnectionStrings:ProductReadReplica"] = "Data Source=missing-health-directory/db.sqlite;Mode=ReadOnly",
            ["ProductReads:Mode"] = mode,
            ["Caching:Provider"] = cache
        }).Build();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Configure<HealthCheckServiceOptions>(options => options.Registrations.Clear());
            services.AddOperationalHealthChecks(config);
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache>(_ => cache == "Redis" ? probeCache : throw new InvalidOperationException("Cache must not be resolved"));
        }));
        using var client = host.CreateClient();
        using var scope = host.Services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        Assert.Equal(HealthStatus.Healthy, report.Entries["database-primary"].Status);
        Assert.Equal(replicaFailure, report.Entries.ContainsKey("database-replica"));
        Assert.Equal(redisFailure, report.Entries.ContainsKey("redis"));
        if (replicaFailure) Assert.Equal(HealthStatus.Unhealthy, report.Entries["database-replica"].Status);
        if (redisFailure) Assert.Equal(HealthStatus.Unhealthy, report.Entries["redis"].Status);
        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(replicaFailure || redisFailure ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(replicaFailure || redisFailure ? "Unhealthy" : "Healthy", await ready.Content.ReadAsStringAsync());
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());
        if (redisFailure) Assert.Equal("__health_probe__", probeCache.Key);
    }

    [Fact]
    public async Task FailedDatabase_AffectsReadinessNotLiveness_AndHidesDetails()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration("database-primary", _ =>
                    new DatabaseHealthCheck(new ReadDbConnectionFactory("Data Source=missing-health-directory/db.sqlite;Mode=ReadOnly", DatabaseProvider.SqliteInMemory), 1),
                    HealthStatus.Unhealthy, ["ready"], TimeSpan.FromSeconds(1)));
            })));
        using var client = host.CreateClient();
        foreach (var path in new[] { "/health/live", "/health/ready", "/health" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("X-Api-Version", "invalid");
            using var response = await client.SendAsync(request);
            var live = path.EndsWith("live", StringComparison.Ordinal);
            Assert.Equal(live ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(live ? "Healthy" : "Unhealthy", await response.Content.ReadAsStringAsync());
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
    }

    [Fact]
    public async Task RedisProbe_OnlyReadsConstantKeyAndPropagatesCancellationAndFailures()
    {
        var cache = new ProbeCache();
        using var source = new CancellationTokenSource();
        var check = new RedisHealthCheck(cache);
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext(), source.Token)).Status);
        Assert.Equal("__health_probe__", cache.Key);
        Assert.Equal(source.Token, cache.Token);
        cache.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => check.CheckHealthAsync(new HealthCheckContext(), source.Token));
    }

    private sealed class ProbeCache : IDistributedCache
    {
        public string? Key { get; private set; }
        public CancellationToken Token { get; private set; }
        public bool Fail { get; set; }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            Key = key;
            Token = token;
            if (Fail) throw new InvalidOperationException("secret");
            return Task.FromResult<byte[]?>(null);
        }
        public byte[]? Get(string key) => throw new NotSupportedException();
        public void Refresh(string key) => throw new NotSupportedException();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
        public void Remove(string key) => throw new NotSupportedException();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("Primary", "Memory", 1)]
    [InlineData("Replica", "Memory", 2)]
    [InlineData("Replica", "Redis", 3)]
    public void Registration_TracksConfiguredDependencies(string mode, string cache, int expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseProvider"] = "PostgreSql",
            ["ConnectionStrings:DefaultConnection"] = "primary",
            ["ConnectionStrings:ProductReadReplica"] = "replica",
            ["ProductReads:Mode"] = mode,
            ["Caching:Provider"] = cache,
            ["HealthChecks:TimeoutSeconds"] = "2"
        }).Build();
        var services = new ServiceCollection();
        services.AddOperationalHealthChecks(config);
        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        Assert.Equal(expected, registrations.Count);
        Assert.All(registrations, check => { Assert.Contains("ready", check.Tags); Assert.Equal(TimeSpan.FromSeconds(2), check.Timeout); });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("61")]
    [InlineData("invalid")]
    public void InvalidTimeout_FailsAtRegistration(string timeout)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["HealthChecks:TimeoutSeconds"] = timeout }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddOperationalHealthChecks(config));
    }
}
