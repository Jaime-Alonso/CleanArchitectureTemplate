using CleanTemplate.Api.Security.Options;
using Microsoft.Extensions.Configuration;

namespace CleanTemplate.Api.IntegrationTests.Security;

public sealed class AuthOptionsValidationTests
{
    [Theory]
    [InlineData("Schemes:LocalJwt:AccessTokenMinutes", "0")]
    [InlineData("Schemes:LocalJwt:AccessTokenMinutes", "1441")]
    [InlineData("Schemes:LocalJwt:RefreshTokenDays", "91")]
    [InlineData("Schemes:LocalJwt:AbsoluteSessionDays", "0")]
    [InlineData("Schemes:LocalJwt:ActiveSigningKeyId", "")]
    public void InvalidLocalOptionsFail(string key, string value)
        => Assert.True(Validate(new() { [key] = value }).Failed);

    [Theory]
    [InlineData("https://user:secret@example.com")]
    [InlineData("https://example.com/?token=secret")]
    [InlineData("https://example.com/#secret")]
    [InlineData("http://example.com")]
    public void InvalidAuthorityFails(string authority)
        => Assert.True(Validate(new() { ["Schemes:Oidc:Enabled"] = "true", ["Schemes:Oidc:Authority"] = authority, ["Schemes:Oidc:Audience"] = "aud" }).Failed);

    [Fact]
    public void DisabledLocalJwtDoesNotRequireItsSecretsOrLifetimes()
        => Assert.True(Validate(new() { ["Schemes:LocalJwt:Enabled"] = "false", ["Schemes:LocalJwt:SigningKey"] = "", ["Schemes:LocalJwt:RefreshTokenDays"] = "0", ["Schemes:Oidc:Enabled"] = "true", ["Schemes:Oidc:Authority"] = "https://example.com", ["Schemes:Oidc:Audience"] = "aud" }).Succeeded);

    [Fact]
    public void DuplicateSigningIdsFail()
        => Assert.True(Validate(new() { ["Schemes:LocalJwt:SigningKeys:0:KeyId"] = "v1", ["Schemes:LocalJwt:SigningKeys:0:Value"] = new string('a', 32), ["Schemes:LocalJwt:SigningKeys:1:KeyId"] = "v1", ["Schemes:LocalJwt:SigningKeys:1:Value"] = new string('b', 32) }).Failed);

    [Fact]
    public void ExternalRolesRemainUnrestrictedButDefaultRoleIsInternal()
    {
        var config = new Dictionary<string, string?> { ["Schemes:Oidc:Enabled"] = "true", ["Schemes:Oidc:Authority"] = "https://example.com", ["Schemes:Oidc:Audience"] = "aud", ["Provisioning:Enabled"] = "true", ["Provisioning:Admin:Roles:0"] = "external-operator" };
        Assert.True(Validate(config).Succeeded);
        config["Provisioning:DefaultRole"] = "external-operator";
        Assert.True(Validate(config).Failed);
        config["Provisioning:DefaultRole"] = "User";
        config["Provisioning:Admin:Roles:0"] = " ";
        Assert.True(Validate(config).Failed);
    }

    [Theory]
    [InlineData("Emails", "not-an-email")]
    [InlineData("Emails", "Person <a@example.com>")]
    [InlineData("Emails", "https://example.com")]
    [InlineData("Domains", "example.com/path")]
    [InlineData("Domains", "example .com")]
    [InlineData("Domains", "user@example.com")]
    public void MalformedProvisioningEntriesFail(string kind, string value)
    {
        var config = new Dictionary<string, string?> { ["Schemes:Oidc:Enabled"] = "true", ["Schemes:Oidc:Authority"] = "https://example.com", ["Schemes:Oidc:Audience"] = "aud", ["Provisioning:Enabled"] = "true", [$"Provisioning:Admin:{kind}:0"] = value };
        Assert.True(Validate(config).Failed);
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?> { ["Schemes:LocalJwt:Issuer"] = "issuer", ["Schemes:LocalJwt:Audience"] = "audience", ["Schemes:LocalJwt:SigningKey"] = new string('a', 32), ["Provisioning:Enabled"] = "false" };
        foreach (var (key, value) in overrides) values[key] = value;
        var options = new ConfigurationBuilder().AddInMemoryCollection(values).Build().Get<AuthOptions>()!;
        return new AuthOptionsValidator().Validate(null, options);
    }
}
