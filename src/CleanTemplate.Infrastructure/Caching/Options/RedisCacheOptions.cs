namespace CleanTemplate.Infrastructure.Caching.Options;

public sealed class RedisCacheOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public string InstanceName { get; init; } = "CleanTemplate:";
}
