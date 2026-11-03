using CleanTemplate.Infrastructure.Caching;
using CleanTemplate.Infrastructure.Caching.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Tests.Database;

public sealed class RedisCacheFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetFailure_DoesNotInvokeFactory(bool canceled)
    {
        using var cts = new CancellationTokenSource();
        if (canceled) cts.Cancel();
        var cache = new FailingCache { FailGet = true };
        var service = new RedisCacheService(cache, Options.Create(new CacheOptions()));
        var calls = 0;
        var exception = await Record.ExceptionAsync(() => service.GetOrSetAsync("key", _ => { calls++; return Task.FromResult("value"); }, cancellationToken: cts.Token));
        AssertFailure(exception, canceled);
        Assert.Equal(0, calls);
        Assert.Equal(1, cache.Gets);
        Assert.Equal(0, cache.Sets);
        Assert.Equal(cts.Token, cache.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetFailure_InvokesFactoryOnceWithoutRetry(bool canceled)
    {
        using var cts = new CancellationTokenSource();
        var cache = new FailingCache { FailSet = true };
        var service = new RedisCacheService(cache, Options.Create(new CacheOptions()));
        var calls = 0;
        var exception = await Record.ExceptionAsync(() => service.GetOrSetAsync("key", token =>
        {
            Assert.Equal(cts.Token, token);
            calls++;
            if (canceled) cts.Cancel();
            return Task.FromResult("value");
        }, cancellationToken: cts.Token));
        AssertFailure(exception, canceled);
        Assert.Equal(1, calls);
        Assert.Equal(1, cache.Gets);
        Assert.Equal(1, cache.Sets);
        Assert.Equal(cts.Token, cache.Token);
    }

    private static void AssertFailure(Exception? exception, bool canceled)
    {
        if (canceled) Assert.IsAssignableFrom<OperationCanceledException>(exception);
        else Assert.IsType<InvalidOperationException>(exception);
    }

    private sealed class FailingCache : IDistributedCache
    {
        public bool FailGet { get; init; }
        public bool FailSet { get; init; }
        public int Gets { get; private set; }
        public int Sets { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            Gets++;
            Token = token;
            token.ThrowIfCancellationRequested();
            if (FailGet) throw new InvalidOperationException();
            return Task.FromResult<byte[]?>(null);
        }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Sets++;
            Token = token;
            token.ThrowIfCancellationRequested();
            if (FailSet) throw new InvalidOperationException();
            return Task.CompletedTask;
        }
        public byte[]? Get(string key) => throw new NotSupportedException();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException();
        public void Refresh(string key) => throw new NotSupportedException();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
        public void Remove(string key) => throw new NotSupportedException();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
    }
}
