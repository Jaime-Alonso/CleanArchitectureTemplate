using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using CleanTemplate.Api.Endpoints;
using CleanTemplate.Api.Extensions;
using CleanTemplate.Api.Security.Extensions;
using CleanTemplate.Application.Contracts.Identity;
using CleanTemplate.Application.Extensions;
using CleanTemplate.Application.Security;
using CleanTemplate.Application.Security.Commands.RefreshToken;
using CleanTemplate.Infrastructure.Extensions;
using CleanTemplate.Infrastructure.Identity;
using Mediora;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CleanTemplate.Api.IntegrationTests.Security;

public sealed class RoleRevocationEndpointsTests
{
    private const string ExternalIssuer = "https://role-issuer.test";
    private const string ExternalKey = "test-only-external-role-signing-key-1234567890123456";

    [Fact]
    public async Task ExternalDemotionAndManualChanges_AffectAnAlreadyIssuedLocalToken()
    {
        var database = $"Data Source=role-revocation-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        // Supply the complete configuration before registrations, as in an actual startup snapshot.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseProvider"] = "SqliteInMemory",
            ["ConnectionStrings:DefaultConnection"] = database,
            ["ConnectionStrings:ReadConnection"] = database,
            ["IdentitySeed:AdminPassword"] = "test-only-SeedPassword_123!",
            ["Auth:Schemes:LocalJwt:Enabled"] = "true",
            ["Auth:Schemes:LocalJwt:Issuer"] = "Local.RoleRevocationTests",
            ["Auth:Schemes:LocalJwt:Audience"] = "role-revocation-tests",
            ["Auth:Schemes:LocalJwt:SigningKey"] = "test-only-local-role-signing-key-12345678901234567890",
            ["Auth:Schemes:Oidc:Enabled"] = "true",
            ["Auth:Schemes:Oidc:Authority"] = ExternalIssuer,
            ["Auth:Schemes:Oidc:Audience"] = "role-revocation-tests",
            ["Auth:Provisioning:Enabled"] = "true",
            ["Auth:Provisioning:Admin:Roles:0"] = "external-admin"
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddApiVersioningConfiguration();
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddApiSecurity(builder.Configuration);
        builder.Services.PostConfigure<JwtBearerOptions>("Oidc", options =>
        {
            var metadata = new OpenIdConnectConfiguration { Issuer = ExternalIssuer };
            metadata.SigningKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ExternalKey)));
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
        });
        await using var app = builder.Build();
        await app.Services.InitializeIdentityAsync();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapProductEndpoints();
        await app.StartAsync();
        using var externalClient = app.GetTestClient();
        using var localClient = app.GetTestClient();
        externalClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExternalToken(admin: true));
        var payload = new { Name = "Role revocation test", Price = 1m, Stock = 1 };

        using var externalAdmin = await externalClient.PostAsJsonAsync("/api/products/", payload);
        Assert.Equal(HttpStatusCode.Created, externalAdmin.StatusCode);

        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var authUsers = scope.ServiceProvider.GetRequiredService<IAuthUserService>();
        var user = await users.FindByEmailAsync("role-user@example.test");
        Assert.NotNull(user);
        Assert.Empty(await users.GetRolesAsync(user)); // External privileges are not manual Identity assignments.
        var localToken = scope.ServiceProvider.GetRequiredService<ILocalJwtTokenGenerator>()
            .Generate(new AuthUser { Id = user.Id, Email = user.Email }, await authUsers.GetRolesAsync(user.Id));
        var refreshToken = await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>()
            .IssueAsync(user.Id, localToken.JwtId, null);
        localClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", localToken.AccessToken);
        using var localAdmin = await localClient.PostAsJsonAsync("/api/products/", payload);
        Assert.Equal(HttpStatusCode.Created, localAdmin.StatusCode);

        externalClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExternalToken(admin: false));
        using var externalDemoted = await externalClient.GetAsync("/api/products/");
        Assert.Equal(HttpStatusCode.OK, externalDemoted.StatusCode);
        using var localDemoted = await localClient.PostAsJsonAsync("/api/products/", payload);
        Assert.Equal(HttpStatusCode.Forbidden, localDemoted.StatusCode); // The original local JWT still contains Admin.
        Assert.Equal(["User"], await authUsers.GetRolesAsync(user.Id));

        var refreshed = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RefreshTokenCommand
        {
            RefreshToken = refreshToken.RefreshToken
        });
        Assert.True(refreshed.IsSuccess);
        var refreshedClaims = new JwtSecurityTokenHandler().ReadJwtToken(refreshed.Value.AccessToken).Claims;
        Assert.Contains(refreshedClaims, claim => claim.Type == ClaimTypes.Role && claim.Value == "User");
        Assert.DoesNotContain(refreshedClaims, claim => claim.Type == ClaimTypes.Role && claim.Value == "Admin");

        Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
        using var externalWithManualAdmin = await externalClient.PostAsJsonAsync("/api/products/", payload);
        using var localWithManualAdmin = await localClient.PostAsJsonAsync("/api/products/", payload);
        Assert.Equal(HttpStatusCode.Created, externalWithManualAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Created, localWithManualAdmin.StatusCode);

        Assert.True((await users.RemoveFromRoleAsync(user, "Admin")).Succeeded);
        using var localRevokedAgain = await localClient.PostAsJsonAsync("/api/products/", payload);
        using var externalRevokedAgain = await externalClient.PostAsJsonAsync("/api/products/", payload);
        Assert.Equal(HttpStatusCode.Forbidden, localRevokedAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, externalRevokedAgain.StatusCode);
    }

    private static string ExternalToken(bool admin)
    {
        List<Claim> claims =
        [
            new Claim("sub", "role-user-subject"),
            new Claim("email", "role-user@example.test"),
            new Claim("email_verified", "true", ClaimValueTypes.Boolean)
        ];
        if (admin)
        {
            claims.Add(new Claim("roles", "external-admin"));
        }

        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: ExternalIssuer,
            audience: "role-revocation-tests",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ExternalKey)), SecurityAlgorithms.HmacSha256)));
    }
}
