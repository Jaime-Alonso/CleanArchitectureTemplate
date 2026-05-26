using System.Text.Json;
using CleanTemplate.Application.Contracts;
using CleanTemplate.Infrastructure.Caching.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Caching;

public sealed class MemoryCacheService : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IMemoryCache _memoryCache;
    private readonly CacheOptions _options;

    public MemoryCacheService(IMemoryCache memoryCache, IOptions<CacheOptions> options)
    {
        _memoryCache = memoryCache;
        _options = options.Value;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_memoryCache.TryGetValue(GetKey(key), out string? payload) || string.IsNullOrWhiteSpace(payload))
        {
            return Task.FromResult<T?>(default);
        }

        return Task.FromResult(JsonSerializer.Deserialize<T>(payload, SerializerOptions));
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.Serialize(value, SerializerOptions);
        var expiration = ttl ?? TimeSpan.FromSeconds(Math.Max(1, _options.DefaultTtlSeconds));

        _memoryCache.Set(GetKey(key), payload, expiration);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _memoryCache.Remove(GetKey(key));
        return Task.CompletedTask;
    }

    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var value = await factory(cancellationToken).ConfigureAwait(false);
        await SetAsync(key, value, ttl, cancellationToken).ConfigureAwait(false);

        return value;
    }

    private string GetKey(string key)
    {
        return $"{_options.KeyPrefix}:{key}";
    }
}
