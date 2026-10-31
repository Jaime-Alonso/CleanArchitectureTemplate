using CleanTemplate.Api.Security.Options;
using Microsoft.Extensions.Configuration;

namespace CleanTemplate.Api.IntegrationTests.Security;

public sealed class OidcTrustConfigurationTests
{
    [Theory]
    [InlineData("Auth:Schemes:Oidc:Claims:Subject", "email")]
    [InlineData("Auth:Schemes:Oidc:Claims:Email", "")]
    [InlineData("Auth:Schemes:Oidc:Claims:EmailVerified", "")]
    [InlineData("Auth:Schemes:Oidc:Claims:TenantId", "")]
    [InlineData("Auth:Schemes:Oidc:AllowedTenantIds:0", " ")]
    public void InvalidProvisioningTrustConfiguration_IsRejected(string key, string value)
    {
        var values = new Dictionary<string, string?>
        {
            ["Auth:Schemes:LocalJwt:Enabled"] = "false",
            ["Auth:Schemes:Oidc:Enabled"] = "true",
            ["Auth:Schemes:Oidc:Authority"] = "https://identity.test",
            ["Auth:Schemes:Oidc:Audience"] = "api-tests",
            ["Auth:Schemes:Oidc:AllowedTenantIds:0"] = "tenant-1",
            ["Auth:Provisioning:Enabled"] = "true"
        };
        values[key] = value;
        var options = new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Auth").Get<AuthOptions>()!;

        Assert.True(new AuthOptionsValidator().Validate(null, options).Failed);
    }
}
