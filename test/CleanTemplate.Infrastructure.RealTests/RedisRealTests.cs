using System.Security.Cryptography;
using CleanTemplate.Application.Contracts.Caching;
using CleanTemplate.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace CleanTemplate.Infrastructure.RealTests;

[Trait("Category", "InfrastructureReal")]
public sealed class RedisRealTests
{
    [Fact]
    public async Task ConcurrentRedisInstancesKeepIdenticalKeysIsolated()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        await Task.WhenAll(Exercise("first"), Exercise("second"));

        async Task Exercise(string value)
        {
            try
            {
                await ExerciseCore(value);
            }
            catch
            {
                ready.TrySetCanceled();
                throw;
            }
        }

        async Task ExerciseCore(string value)
        {
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await using var redis = new RedisBuilder("redis:7-alpine")
                .WithCommand("redis-server", "--requirepass", password)
                .Build();
            await redis.StartAsync();
            await using var services = CreateServices(redis.GetConnectionString(), password, "same-prefix");
            var cache = services.GetRequiredService<ICacheService>();
            await cache.SetAsync("same-key", value);
            if (Interlocked.Increment(ref started) == 2)
                ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromMinutes(2));
            Assert.Equal(value, await cache.GetAsync<string>("same-key"));
            await cache.RemoveAsync("same-key");
            Assert.Null(await cache.GetAsync<string>("same-key"));
        }
    }

    [Fact]
    public async Task RoundtripAndUnavailableRedisDoesNotRepeatFactory()
    {
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var redis = new RedisBuilder("redis:7-alpine")
            .WithCommand("redis-server", "--requirepass", password)
            .Build();
        await redis.StartAsync();
        var prefix = Guid.NewGuid().ToString("N");
        await using var services = CreateServices(redis.GetConnectionString(), password, prefix);
        var cache = services.GetRequiredService<ICacheService>();
        var calls = 0;
        async Task<string> Factory(CancellationToken _) { calls++; await Task.Yield(); return "value"; }
        Assert.Equal("value", await cache.GetOrSetAsync("key", Factory));
        Assert.Equal("value", await cache.GetOrSetAsync("key", Factory));
        Assert.Equal(1, calls);
        await redis.StopAsync();
        var error = await Record.ExceptionAsync(() => cache.GetOrSetAsync("other", Factory));
        Assert.True(error is RedisConnectionException or RedisTimeoutException);
        Assert.Equal(1, calls);
    }

    private static ServiceProvider CreateServices(string connectionString, string password, string prefix)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseProvider"] = "SqliteInMemory",
            ["Caching:Provider"] = "Redis",
            ["Caching:KeyPrefix"] = prefix,
            ["Caching:Redis:InstanceName"] = prefix + ":",
            ["Caching:Redis:ConnectionString"] = connectionString + ",password=" + password + ",connectTimeout=1000,syncTimeout=1000,asyncTimeout=1000,connectRetry=0,abortConnect=false"
        }).Build();
        return new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
    }
}
