using System.Text.Json;
using CleanTemplate.Api.Security.Extensions;
using CleanTemplate.Api.Security.Options;
using CleanTemplate.Infrastructure.Extensions;
using CleanTemplate.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Api.IntegrationTests.Security;

public sealed class DevelopmentSecretsConfigurationTests
{
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void ShippedConfiguration_DoesNotContainUsableCredentials(string fileName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, fileName)));
        var root = document.RootElement;
        var signingKey = root.GetProperty("Auth").GetProperty("Schemes").GetProperty("LocalJwt").GetProperty("SigningKey").GetString();
        var password = root.GetProperty("IdentitySeed").GetProperty("AdminPassword").GetString();

        Assert.True(string.IsNullOrEmpty(signingKey));
        Assert.StartsWith("__SET_VIA_", password);
    }

    [Fact]
    public void DevelopmentWithoutExternalSecrets_RejectsSigningKeyAndSeedPassword()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json")
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddInfrastructure(configuration);
        services.AddApiSecurity(configuration);
        using var provider = services.BuildServiceProvider();

        var authError = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<AuthOptions>>().Value);
        var seedError = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<IdentitySeedOptions>>().Value);

        Assert.Contains("SigningKey", authError.Message);
        Assert.Contains("placeholder", seedError.Message);
    }
}
