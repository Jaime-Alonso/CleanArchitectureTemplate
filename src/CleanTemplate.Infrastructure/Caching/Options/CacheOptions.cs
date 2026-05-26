namespace CleanTemplate.Infrastructure.Caching.Options;

public sealed class CacheOptions
{
    public const string SectionName = "Caching";

    public CacheProvider Provider { get; init; } = CacheProvider.Memory;
    public int DefaultTtlSeconds { get; init; } = 300;
    public string KeyPrefix { get; init; } = "CleanTemplate";
    public RedisCacheOptions Redis { get; init; } = new();
}
