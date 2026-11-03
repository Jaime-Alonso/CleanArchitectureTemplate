using CleanTemplate.Application.Contracts.Caching;
using CleanTemplate.Infrastructure.Caching;
using CleanTemplate.Infrastructure.Caching.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Infrastructure.Extensions;

public static partial class ServiceCollectionExtensions
{
    private static void AddCachingOptions(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<CacheOptions>()
            .Bind(configuration.GetSection(CacheOptions.SectionName))
            .Validate(options => Enum.IsDefined(options.Provider), "Caching:Provider must be a defined provider.")
            .Validate(options => options.DefaultTtlSeconds > 0, "Caching:DefaultTtlSeconds must be greater than zero.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.KeyPrefix), "Caching:KeyPrefix must be configured.")
            .Validate(
                options => options.Provider != CacheProvider.Redis || !string.IsNullOrWhiteSpace(options.Redis.ConnectionString),
                "Caching:Redis:ConnectionString must be configured when Caching:Provider is Redis.")
            .ValidateOnStart();

    }

    private static void AddCaching(IServiceCollection services, CacheOptions cacheOptions)
    {
        if (!Enum.IsDefined(cacheOptions.Provider))
            throw new InvalidOperationException("Caching:Provider must be a defined provider.");
        if (cacheOptions.DefaultTtlSeconds <= 0)
            throw new InvalidOperationException("Caching:DefaultTtlSeconds must be greater than zero.");
        if (string.IsNullOrWhiteSpace(cacheOptions.KeyPrefix))
            throw new InvalidOperationException("Caching:KeyPrefix must be configured.");
        switch (cacheOptions.Provider)
        {
            case CacheProvider.Memory:
                services.AddMemoryCache();
                services.AddSingleton<ICacheService, MemoryCacheService>();
                break;
            case CacheProvider.Redis:
                if (string.IsNullOrWhiteSpace(cacheOptions.Redis.ConnectionString))
                {
                    throw new InvalidOperationException("Caching:Redis:ConnectionString must be configured when Caching:Provider is Redis.");
                }

                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = cacheOptions.Redis.ConnectionString;
                    options.InstanceName = cacheOptions.Redis.InstanceName;
                });
                services.AddSingleton<ICacheService, RedisCacheService>();
                break;
            default:
                throw new InvalidOperationException("Caching:Provider must be a defined provider.");
        }

    }
}
