using CleanTemplate.Crosscutting.RateLimiting;
using CleanTemplate.Crosscutting.RateLimiting.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

public sealed class RateLimitingConfigurationTests
{
    [Theory]
    [InlineData("RateLimiting:Global:PermitLimit", "0")]
    [InlineData("RateLimiting:Global:WindowSeconds", "0")]
    [InlineData("RateLimiting:Policies:auth.login:QueueLimit", "-1")]
    [InlineData("RateLimiting:Global:WindowSeconds", "4294968")]
    [InlineData("RateLimiting:Global:WindowSeconds", "2147483647")]
    [InlineData("RateLimiting:Policies:auth.login:WindowSeconds", "4294968")]
    [InlineData("RateLimiting:Policies:auth.login:WindowSeconds", "2147483647")]
    public void InvalidLimitsFailBeforeRegistration(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [key] = value
        }).Build();

        var exception = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddApiRateLimiting(configuration));
        Assert.Contains(key, exception.Message);
    }

    [Fact]
    public void MaximumWindowIsAcceptedAndValidatorRejectsAboveMaximum()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Global:WindowSeconds"] = "4294967",
            ["RateLimiting:Policies:auth.login:WindowSeconds"] = "4294967"
        }).Build();
        var services = new ServiceCollection();
        services.AddApiRateLimiting(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.Equal(4294967, provider.GetRequiredService<IOptions<ApiRateLimitingOptions>>().Value.Global.WindowSeconds);
        var validator = provider.GetRequiredService<IValidateOptions<ApiRateLimitingOptions>>();
        var invalid = new ApiRateLimitingOptions { Global = new FixedWindowPolicyOptions { WindowSeconds = int.MaxValue } };
        Assert.True(validator.Validate(null, invalid).Failed);
    }

    [Fact]
    public void ValidLimitsAreAvailableThroughValidatedOptions()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Global:PermitLimit"] = "1",
            ["RateLimiting:Global:WindowSeconds"] = "1",
            ["RateLimiting:Global:QueueLimit"] = "0"
        }).Build();
        var services = new ServiceCollection();
        services.AddApiRateLimiting(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(1, provider.GetRequiredService<IOptions<ApiRateLimitingOptions>>().Value.Global.PermitLimit);
    }
}
