using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using CleanTemplate.Api.Security.Extensions;
using CleanTemplate.Application.Contracts.Identity;
using CleanTemplate.Application.Security;
using CleanTemplate.SharedKernel.Errors;
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
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CleanTemplate.Api.IntegrationTests.Security;

public sealed class InternalAuthorizationTests
{
    private const string LocalIssuer = "https://local.test";
    private const string ExternalIssuer = "https://external.test";
    private const string Audience = "authorization-tests";
    private const string SigningKey = "test-only-signing-key-for-authorization-1234567890";
    private static readonly Guid InternalUserId = Guid.Parse("3595929f-cd18-4753-ad16-b9967ea65991");

    [Fact]
    public async Task ExternalClaimsCannotImpersonateAnInternalAdministrator()
    {
        var provisioning = new ProvisioningStub();
        using var host = CreateHost(provisioning);
        using var client = Client(host, ExternalToken(SpoofedClaims()));

        using var read = await client.GetAsync("/read");
        using var write = await client.PostAsync("/write", null);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal($"{InternalUserId}|False", await read.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(2, provisioning.Calls);
        Assert.Equal("external-subject", provisioning.LastIdentity?.ProviderSubject);
        Assert.Equal(ExternalIssuer, provisioning.LastIdentity?.Issuer);
    }

    [Fact]
    public async Task ProvisioningDisabled_ExternalRolesAndInternalMarkersGrantNoPermissions()
    {
        var provisioning = new ProvisioningStub();
        using var host = CreateHost(provisioning, provisioningEnabled: false);
        using var client = Client(host, ExternalToken(SpoofedClaims()));

        using var read = await client.GetAsync("/read");
        using var write = await client.PostAsync("/write", null);
        using var authenticated = await client.GetAsync("/authenticated");

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        Assert.Equal("False", await authenticated.Content.ReadAsStringAsync());
        Assert.Equal(0, provisioning.Calls);
    }

    [Fact]
    public async Task FailedProvisioning_DoesNotRetainExternalAdministratorRoles()
    {
        var provisioning = new ProvisioningStub { Reject = true };
        using var host = CreateHost(provisioning);
        using var client = Client(host, ExternalToken(SpoofedClaims()));

        using var write = await client.PostAsync("/write", null);
        using var authenticated = await client.GetAsync("/authenticated");

        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal("False", await authenticated.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("not-a-boolean")]
    public async Task EmailVerification_IsNeverInferredFromEmailPresence(string? verification)
    {
        var provisioning = new ProvisioningStub();
        using var host = CreateHost(provisioning);
        using var client = Client(host, ExternalToken([], verification));

        using var response = await client.GetAsync("/read");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(provisioning.LastIdentity);
        Assert.False(provisioning.LastIdentity.EmailVerified);
    }

    [Fact]
    public async Task ConflictingVerificationClaims_AreNotTreatedAsVerified()
    {
        var provisioning = new ProvisioningStub();
        using var host = CreateHost(provisioning);
        using var client = Client(host, ExternalToken([new Claim("email_verified", "false")]));

        using var response = await client.GetAsync("/read");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(provisioning.LastIdentity);
        Assert.False(provisioning.LastIdentity.EmailVerified);
    }

    [Fact]
    public async Task MissingSubject_DoesNotProvision()
    {
        var provisioning = new ProvisioningStub();
        using var host = CreateHost(provisioning);
        using var client = Client(host, Token(ExternalIssuer,
        [
            new Claim("email", "user@example.test"),
            new Claim("email_verified", "true")
        ]));

        using var response = await client.GetAsync("/read");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, provisioning.Calls);
    }

    [Fact]
    public async Task AdministratorResolvedInternally_CanWrite()
    {
        var provisioning = new ProvisioningStub { Roles = ["Admin"] };
        using var host = CreateHost(provisioning);
        using var client = Client(host, ExternalToken([]));

        using var response = await client.PostAsync("/write", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("User", HttpStatusCode.Forbidden)]
    [InlineData("Admin", HttpStatusCode.OK)]
    public async Task LocalJwt_UsesValidatedSubjectAndCurrentInternalRoles(string role, HttpStatusCode expectedWriteStatus)
    {
        var provisioning = new ProvisioningStub();
        using var host = CreateHost(provisioning, authorization: new AuthorizationStub { Roles = [role] });
        var localUserId = Guid.NewGuid();
        using var client = Client(host, Token(LocalIssuer,
        [
            new Claim("sub", localUserId.ToString()),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(InternalClaimTypes.InternalUserId, Guid.NewGuid().ToString())
        ]));

        using var read = await client.GetAsync("/read");
        using var write = await client.PostAsync("/write", null);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.StartsWith(localUserId.ToString(), await read.Content.ReadAsStringAsync());
        Assert.Equal(expectedWriteStatus, write.StatusCode);
        Assert.Equal(0, provisioning.Calls);
    }

    [Fact]
    public async Task ExistingLocalToken_LosesAdministratorAccessOnTheNextRequest()
    {
        var authorization = new AuthorizationStub { Roles = ["Admin"] };
        using var host = CreateHost(new ProvisioningStub(), authorization: authorization);
        using var client = Client(host, Token(LocalIssuer,
        [
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        ]));
        using var before = await client.PostAsync("/write", null);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        authorization.Roles = ["User"];

        using var after = await client.PostAsync("/write", null);
        using var read = await client.GetAsync("/read");
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task MissingOrLockedLocalAccount_CannotUseItsSignedAdministratorClaims()
    {
        using var host = CreateHost(new ProvisioningStub(), authorization: new AuthorizationStub { Unavailable = true });
        using var client = Client(host, Token(LocalIssuer,
        [
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        ]));

        using var response = await client.PostAsync("/write", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PermissionStoreFailure_DoesNotFallBackToSignedTokenRoles()
    {
        using var host = CreateHost(new ProvisioningStub(), authorization: new AuthorizationStub { Fail = true });
        using var client = Client(host, Token(LocalIssuer,
        [
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        ]));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var response = await client.PostAsync("/write", null);
        });

        Assert.Equal("Permission store unavailable.", error.Message);
    }

    [Theory]
    [InlineData("tenant-1", HttpStatusCode.OK)]
    [InlineData("tenant-2", HttpStatusCode.Unauthorized)]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    public async Task TenantRestriction_IsEnforcedBeforeProvisioning(string? tenant, HttpStatusCode expectedStatus)
    {
        var provisioning = new ProvisioningStub();
        using var host = CreateHost(provisioning, allowedTenant: "tenant-1");
        var claims = tenant is null ? Array.Empty<Claim>() : new[] { new Claim("tid", tenant) };
        using var client = Client(host, ExternalToken(claims));

        using var response = await client.GetAsync("/read");

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedStatus == HttpStatusCode.OK ? 1 : 0, provisioning.Calls);
    }

    [Fact]
    public async Task DuplicateTenantClaim_IsRejected()
    {
        var provisioning = new ProvisioningStub();
        using var host = CreateHost(provisioning, allowedTenant: "tenant-1");
        using var client = Client(host, ExternalToken([new Claim("tid", "tenant-1"), new Claim("tid", "tenant-2")]));

        using var response = await client.GetAsync("/read");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, provisioning.Calls);
    }

    [Fact]
    public async Task DuplicateSubject_DoesNotProvisionOrAuthorize()
    {
        var provisioning = new ProvisioningStub();
        using var host = CreateHost(provisioning);
        using var client = Client(host, ExternalToken([new Claim("sub", "second-subject")]));

        using var response = await client.GetAsync("/read");

        // The JWT validator rejects an array-valued sub before transformation/provisioning.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, provisioning.Calls);
    }

    [Fact]
    public async Task RepeatedTransformation_IsIdempotentWithoutTrustingATokenMarker()
    {
        var provisioning = new ProvisioningStub();
        using var host = CreateHost(provisioning);
        using var client = Client(host, ExternalToken(SpoofedClaims()));

        using var response = await client.GetAsync("/repeat");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("True", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, provisioning.Calls);
    }

    private static IHost CreateHost(ProvisioningStub provisioning, bool provisioningEnabled = true, string? allowedTenant = null,
        IUserAuthorizationService? authorization = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Schemes:LocalJwt:Enabled"] = "true",
            ["Auth:Schemes:LocalJwt:Issuer"] = LocalIssuer,
            ["Auth:Schemes:LocalJwt:Audience"] = Audience,
            ["Auth:Schemes:LocalJwt:SigningKey"] = SigningKey,
            ["Auth:Schemes:Oidc:Enabled"] = "true",
            ["Auth:Schemes:Oidc:Authority"] = ExternalIssuer,
            ["Auth:Schemes:Oidc:Audience"] = Audience,
            ["Auth:Schemes:Oidc:AllowedTenantIds:0"] = allowedTenant,
            ["Auth:Provisioning:Enabled"] = provisioningEnabled.ToString()
        }.Where(pair => pair.Value is not null)).Build();

        return new HostBuilder().ConfigureWebHost(builder => builder
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddApiSecurity(configuration);
                services.AddSingleton<IExternalIdentityProvisioningService>(provisioning);
                services.AddSingleton<IUserAuthorizationService>(authorization ?? new AuthorizationStub());
                services.PostConfigure<JwtBearerOptions>("Oidc", options =>
                {
                    var metadata = new OpenIdConnectConfiguration { Issuer = ExternalIssuer };
                    metadata.SigningKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)));
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/read", context => context.Response.WriteAsync(
                        $"{context.User.FindFirstValue(InternalClaimTypes.InternalUserId)}|{context.User.IsInRole("Admin")}"))
                        .RequireAuthorization("UserOrAdmin");
                    endpoints.MapPost("/write", _ => Task.CompletedTask).RequireAuthorization("AdminOnly");
                    endpoints.MapGet("/authenticated", context => context.Response.WriteAsync(context.User.IsInRole("Admin").ToString()))
                        .RequireAuthorization();
                    endpoints.MapGet("/repeat", async context =>
                    {
                        var transformation = context.RequestServices.GetRequiredService<IClaimsTransformation>();
                        var second = await transformation.TransformAsync(context.User);
                        await context.Response.WriteAsync(ReferenceEquals(context.User, second).ToString());
                    }).RequireAuthorization("UserOrAdmin");
                });
            })).Start();
    }

    private static HttpClient Client(IHost host, string token)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Claim[] SpoofedClaims() =>
    [
        new Claim(InternalClaimTypes.InternalUserId, Guid.NewGuid().ToString()),
        new Claim(InternalClaimTypes.AuthProvider, "LocalJwt"),
        new Claim(InternalClaimTypes.Role, "Admin"),
        new Claim("INTERNAL_ROLE", "Admin"),
        new Claim(ClaimTypes.Role, "Admin"),
        new Claim("roles", "Admin"),
        new Claim("role", "Admin"),
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
    ];

    private static string ExternalToken(IEnumerable<Claim> extraClaims, string? verification = "true")
    {
        List<Claim> claims = [new Claim("sub", "external-subject"), new Claim("email", "user@example.test")];
        if (verification is not null)
        {
            claims.Add(new Claim("email_verified", verification));
        }

        claims.AddRange(extraClaims);
        return Token(ExternalIssuer, claims);
    }

    private static string Token(string issuer, IEnumerable<Claim> claims)
    {
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256)));
    }

    private sealed class ProvisioningStub : IExternalIdentityProvisioningService
    {
        public int Calls { get; private set; }
        public ExternalIdentity? LastIdentity { get; private set; }
        public IReadOnlyCollection<string> Roles { get; init; } = ["User"];
        public bool Reject { get; init; }

        public Task<Result<ProvisionResult>> ProvisionAsync(ExternalIdentity externalIdentity, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastIdentity = externalIdentity;
            return Task.FromResult(Reject || !externalIdentity.EmailVerified
                ? Result<ProvisionResult>.Failure(new Error("Auth.ProvisioningRejected", "Rejected by test fixture.", ErrorType.Forbidden))
                : Result<ProvisionResult>.Success(new ProvisionResult
                {
                    InternalUserId = InternalUserId,
                    InternalRoles = Roles,
                    ProvisioningOccurred = false
                }));
        }
    }

    private sealed class AuthorizationStub : IUserAuthorizationService
    {
        public IReadOnlyCollection<string> Roles { get; set; } = ["User"];
        public bool Unavailable { get; set; }
        public bool Fail { get; set; }

        public Task<UserAuthorization?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("Permission store unavailable.");
            }

            return Task.FromResult(Unavailable ? null : new UserAuthorization(userId, Roles));
        }
    }
}
