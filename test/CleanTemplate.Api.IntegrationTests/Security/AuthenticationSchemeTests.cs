using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using CleanTemplate.Api.Security.Extensions;
using CleanTemplate.Api.Security.Options;
using CleanTemplate.Application.Contracts;
using CleanTemplate.Application.Security;
using CleanTemplate.SharedKernel.Results;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CleanTemplate.Api.IntegrationTests.Security;

public sealed class AuthenticationSchemeTests
{
    private const string LocalIssuer = "https://local.test";
    private const string ExternalIssuer = "https://identity.test";
    private const string Audience = "security-tests";
    private const string LocalKey = "test-only-local-signing-key-12345678901234567890";
    private const string ExternalKey = "test-only-external-signing-key-1234567890123456";

    [Theory]
    [InlineData(true, false, "LocalJwt")]
    [InlineData(false, true, "Oidc")]
    [InlineData(true, true, "LocalJwt")]
    [InlineData(true, true, "Oidc")]
    public async Task EnabledScheme_AcceptsValidToken(bool localEnabled, bool oidcEnabled, string scheme)
    {
        using var server = CreateServer(localEnabled, oidcEnabled);
        using var client = server.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(scheme));

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(true, false, "Oidc")]
    [InlineData(false, true, "LocalJwt")]
    public async Task DisabledScheme_IsNotRegisteredAndRejectsOtherwiseValidToken(bool localEnabled, bool oidcEnabled, string disabledScheme)
    {
        using var server = CreateServer(localEnabled, oidcEnabled);
        var provider = server.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.Null(await provider.GetSchemeAsync(disabledScheme));

        using var client = server.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(disabledScheme));
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer malformed-token")]
    [InlineData("Bearer ")]
    [InlineData("Basic credentials")]
    public async Task MissingOrMalformedToken_ReturnsBearerChallenge(string? authorization)
    {
        using var server = CreateServer(true, true);
        using var client = server.GetTestClient();
        if (authorization is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization);
        }

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
    }

    [Theory]
    [InlineData("LocalJwt", "signature")]
    [InlineData("Oidc", "signature")]
    [InlineData("LocalJwt", "audience")]
    [InlineData("Oidc", "audience")]
    [InlineData("LocalJwt", "expired")]
    [InlineData("Oidc", "expired")]
    [InlineData("Oidc", "issuer")]
    public async Task EnabledScheme_DoesNotRelaxTokenValidation(string scheme, string invalidPart)
    {
        using var server = CreateServer(true, true);
        using var client = server.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(scheme, invalidPart));

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void NoEnabledSchemes_FailsStartupValidation()
    {
        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            using var host = CreateServer(false, false);
        });

        Assert.Contains("At least one authentication scheme", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LocalAccessTokenLifetime_MustBePositive(int minutes)
    {
        var values = CreateConfiguration(true, false).AsEnumerable().ToDictionary(pair => pair.Key, pair => pair.Value);
        values["Auth:Schemes:LocalJwt:AccessTokenMinutes"] = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var options = new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Auth").Get<AuthOptions>()!;

        var result = new AuthOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("AccessTokenMinutes", result.FailureMessage);
    }

    [Theory]
    [InlineData("http://identity.test")]
    [InlineData("not-a-url")]
    public void OidcAuthority_RequiresAbsoluteHttpsUrl(string authority)
    {
        var values = CreateConfiguration(false, true).AsEnumerable().ToDictionary(pair => pair.Key, pair => pair.Value);
        values["Auth:Schemes:Oidc:Authority"] = authority;
        var options = new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Auth").Get<AuthOptions>()!;

        Assert.True(new AuthOptionsValidator().Validate(null, options).Failed);
    }

    [Fact]
    public void ProvisioningWithoutOidc_IsRejected()
    {
        var options = CreateConfiguration(true, false).GetSection("Auth").Get<AuthOptions>()!;
        options = new AuthOptions
        {
            Schemes = options.Schemes,
            Provisioning = new ExternalRoleMappingOptions { Enabled = true }
        };

        Assert.True(new AuthOptionsValidator().Validate(null, options).Failed);
    }

    private static IHost CreateServer(bool localEnabled, bool oidcEnabled)
    {
        return new HostBuilder().ConfigureWebHost(builder => builder
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddApiSecurity(CreateConfiguration(localEnabled, oidcEnabled));
                services.AddScoped<IExternalIdentityProvisioningService, UnexpectedProvisioningService>();
                services.AddScoped<IUserAuthorizationService, UnexpectedAuthorizationService>();
                services.PostConfigure<JwtBearerOptions>("Oidc", options =>
                {
                    var metadata = new OpenIdConnectConfiguration { Issuer = ExternalIssuer };
                    metadata.SigningKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ExternalKey)));
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                });
            })
            .Configure(app =>
            {
                app.UseAuthentication();
                // This fixture tests authentication only, independently of role provisioning/policies.
                app.Run(async context =>
                {
                    if (context.User.Identity?.IsAuthenticated != true)
                    {
                        await context.ChallengeAsync();
                        return;
                    }

                    context.Response.StatusCode = StatusCodes.Status200OK;
                });
            })).Start();
    }

    private static IConfigurationRoot CreateConfiguration(bool localEnabled, bool oidcEnabled)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Schemes:LocalJwt:Enabled"] = localEnabled.ToString(),
            ["Auth:Schemes:LocalJwt:Issuer"] = LocalIssuer,
            ["Auth:Schemes:LocalJwt:Audience"] = Audience,
            ["Auth:Schemes:LocalJwt:SigningKey"] = LocalKey,
            ["Auth:Schemes:Oidc:Enabled"] = oidcEnabled.ToString(),
            ["Auth:Schemes:Oidc:Authority"] = ExternalIssuer,
            ["Auth:Schemes:Oidc:Audience"] = Audience,
            ["Auth:Provisioning:Enabled"] = "false"
        }).Build();
    }

    private static string CreateToken(string scheme, string? invalidPart = null)
    {
        var key = scheme == "LocalJwt" ? LocalKey : ExternalKey;
        var issuer = scheme == "LocalJwt" ? LocalIssuer : ExternalIssuer;
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: invalidPart == "issuer" ? "https://untrusted.test" : issuer,
            audience: invalidPart == "audience" ? "wrong-audience" : Audience,
            claims: [new Claim("sub", "security-test-user")],
            notBefore: now.AddMinutes(-10),
            expires: invalidPart == "expired" ? now.AddMinutes(-5) : now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(invalidPart == "signature" ? new string('x', 64) : key)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class UnexpectedProvisioningService : IExternalIdentityProvisioningService
    {
        public Task<Result<ProvisionResult>> ProvisionAsync(ExternalIdentity externalIdentity, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Provisioning is disabled in this authentication-only fixture.");
        }
    }

    private sealed class UnexpectedAuthorizationService : IUserAuthorizationService
    {
        public Task<UserAuthorization?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("This fixture uses no valid local user IDs and must not query internal permissions.");
        }
    }
}
