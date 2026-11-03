using CleanTemplate.Application.Contracts.Caching;
using CleanTemplate.Infrastructure.Caching.Options;
using CleanTemplate.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Tests.Database;

public sealed class CacheConfigurationTests
{
    [Theory]
    [InlineData("Caching:Provider", "99", "Caching:Provider")]
    [InlineData("Caching:Provider", "secret-invalid", "Caching")]
    [InlineData("Caching:DefaultTtlSeconds", "0", "Caching:DefaultTtlSeconds")]
    [InlineData("Caching:DefaultTtlSeconds", "-1", "Caching:DefaultTtlSeconds")]
    [InlineData("Caching:KeyPrefix", " ", "Caching:KeyPrefix")]
    [InlineData("Caching:Provider", "Redis", "Caching:Redis:ConnectionString")]
    public void InvalidConfigurationFailsBeforeUse(string key, string value, string expectedKey)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build(new() { [key] = value }));
        Assert.Contains(expectedKey, error.Message);
        Assert.DoesNotContain("secret-invalid", error.Message);
    }

    [Theory]
    [InlineData("Memory", "")]
    [InlineData("Redis", "localhost:6379")]
    public void ValidProvidersRegisterAndValidateOptions(string cacheProvider, string connection)
    {
        using var provider = Build(new() { ["Caching:Provider"] = cacheProvider, ["Caching:Redis:ConnectionString"] = connection, ["Caching:DefaultTtlSeconds"] = "1", ["Caching:KeyPrefix"] = "test" });
        Assert.Equal(1, provider.GetRequiredService<IOptions<CacheOptions>>().Value.DefaultTtlSeconds);
        Assert.NotNull(provider.GetRequiredService<ICacheService>());
    }

    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        values["DatabaseProvider"] = "SqliteInMemory";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
    }
}
