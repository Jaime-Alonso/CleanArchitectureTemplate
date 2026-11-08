using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanTemplate.Api.IntegrationTests.Infrastructure;
using CleanTemplate.Application.Contracts.Identity;
using CleanTemplate.Application.Security;
using CleanTemplate.Domain.Entities;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace CleanTemplate.Api.IntegrationTests.Security;

[Collection(HostIntegrationCollection.Name)]
public sealed class ProductionSecurityTests(ProductionWebApplicationFactory factory) : IClassFixture<ProductionWebApplicationFactory>
{
    [Theory]
    [InlineData("malformed")]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("unsigned")]
    public async Task InvalidJwt_ReturnsSanitizedBearerChallenge(string fault)
    {
        var user = await CreateUser(factory, "Admin");
        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        var token = fault == "malformed" ? "CANARY_MALFORMED_TOKEN" : CreateToken(configuration, user.Id, fault);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync("/api/products/");
        await AssertProblem(response, HttpStatusCode.Unauthorized, "auth.unauthorized", token);
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
        Assert.DoesNotContain("error_description", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task SignedAdministratorClaims_CannotElevatePersistedUser()
    {
        var user = await CreateUser(factory, "User");
        using var client = factory.CreateClient();
        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        // Valid signature, with deliberately false role and internal claims.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(configuration, user.Id));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var product = new Product("protected", null, 1m, 1);
        db.Add(product);
        await db.SaveChangesAsync();
        using var read = await client.GetAsync($"/api/products/{product.Id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var create = await client.PostAsJsonAsync("/api/products/", new { Name = "forbidden", Price = 1m, Stock = 1 });
        using var update = await client.PutAsJsonAsync($"/api/products/{product.Id}", new { Name = "forbidden", Price = 2m, Stock = 2, ExpectedVersion = 1 });
        using var delete = await client.DeleteAsync($"/api/products/{product.Id}?expectedVersion=1");
        foreach (var response in new[] { create, update, delete })
            await AssertProblem(response, HttpStatusCode.Forbidden, "auth.forbidden");
        var stored = await db.Set<Product>().AsNoTracking().SingleAsync(value => value.Id == product.Id);
        Assert.Equal("protected", stored.Name);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task AdministratorRemoval_AppliesToTheNextRequestWithTheSameToken()
    {
        var user = await CreateUser(factory, "Admin");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            CreateToken(factory.Services.GetRequiredService<IConfiguration>(), user.Id));
        using var allowed = await client.PostAsJsonAsync("/api/products/", new { Name = "before-revocation", Price = 1m, Stock = 1 });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.RemoveFromRoleAsync((await users.FindByIdAsync(user.Id.ToString()))!, "Admin")).Succeeded);
        using var rejected = await client.PostAsJsonAsync("/api/products/", new { Name = "after-revocation", Price = 1m, Stock = 1 });
        await AssertProblem(rejected, HttpStatusCode.Forbidden, "auth.forbidden");
    }

    [Fact]
    public async Task PermissionStoreFailure_DoesNotUseSignedAdministratorClaims()
    {
        await using var isolated = new ProductionWebApplicationFactory();
        using var host = isolated.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Scoped<IUserAuthorizationService, FailedAuthorization>())));
        var user = await CreateUser(host, "Admin");
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            CreateToken(host.Services.GetRequiredService<IConfiguration>(), user.Id));
        using var rejected = await client.PostAsJsonAsync("/api/products/", new { Name = "forbidden", Price = 1m, Stock = 1 });
        await AssertProblem(rejected, HttpStatusCode.InternalServerError, "server.error", "CANARY_PERMISSION_FAILURE");
        using var scope = host.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<Product>().AnyAsync());
    }

    [Fact]
    public async Task SigningKeyRotation_AcceptsOverlapAndRejectsRetiredKey()
    {
        var oldKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var newKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var settings = new Dictionary<string, string?>
        {
            ["Database:AllowEphemeralInProduction"] = "true",
            ["Auth:Schemes:LocalJwt:ActiveSigningKeyId"] = "new",
            ["Auth:Schemes:LocalJwt:SigningKeys:0:KeyId"] = "new",
            ["Auth:Schemes:LocalJwt:SigningKeys:0:Value"] = newKey,
            ["Auth:Schemes:LocalJwt:SigningKeys:1:KeyId"] = "old",
            ["Auth:Schemes:LocalJwt:SigningKeys:1:Value"] = oldKey
        };
        string oldToken;
        string newToken;
        Guid userId;
        await using (var overlap = new TestWebApplicationFactory("Production", settings))
        {
            var user = await CreateUser(overlap, "Admin");
            userId = user.Id;
            oldToken = CreateToken(overlap.Services.GetRequiredService<IConfiguration>(), userId, key: oldKey, keyId: "old");
            using var scope = overlap.Services.CreateScope();
            newToken = scope.ServiceProvider.GetRequiredService<ILocalJwtTokenGenerator>()
                .Generate(new AuthUser { Id = userId, Email = user.Email }, ["Admin"]).AccessToken;
            Assert.Equal("new", new JwtSecurityTokenHandler().ReadJwtToken(newToken).Header.Kid);
            using var client = overlap.CreateClient();
            foreach (var token in new[] { oldToken, newToken })
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await client.GetAsync("/api/products/");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        settings.Remove("Auth:Schemes:LocalJwt:SigningKeys:1:KeyId");
        settings.Remove("Auth:Schemes:LocalJwt:SigningKeys:1:Value");
        await using var retired = new TestWebApplicationFactory("Production", settings);
        await CreateUser(retired, "Admin", userId);
        using var retiredClient = retired.CreateClient();
        retiredClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
        using var accepted = await retiredClient.GetAsync("/api/products/");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        retiredClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);
        using var rejected = await retiredClient.GetAsync("/api/products/");
        await AssertProblem(rejected, HttpStatusCode.Unauthorized, "auth.unauthorized", oldToken);
    }

    private static async Task<ApplicationUser> CreateUser(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, string role, Guid? id = null)
    {
        using var scope = host.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(role))
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(role))).Succeeded);
        var user = new ApplicationUser { Id = id ?? Guid.NewGuid(), UserName = $"user-{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.test" };
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.CreateAsync(user)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private static string CreateToken(IConfiguration configuration, Guid userId, string? fault = null, string? key = null, string? keyId = null)
    {
        var now = DateTime.UtcNow;
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(fault == "signature"
            ? new string('x', 64) : key ?? configuration["Auth:Schemes:LocalJwt:SigningKey"]!))
        { KeyId = keyId ?? configuration["Auth:Schemes:LocalJwt:ActiveSigningKeyId"] };
        var token = new JwtSecurityToken(
            issuer: fault == "issuer" ? "https://CANARY_UNTRUSTED_ISSUER.test" : configuration["Auth:Schemes:LocalJwt:Issuer"],
            audience: fault == "audience" ? "CANARY_WRONG_AUDIENCE" : configuration["Auth:Schemes:LocalJwt:Audience"],
            claims: [new Claim("sub", userId.ToString()), new Claim(ClaimTypes.Role, "Admin"),
                new Claim(InternalClaimTypes.Role, "Admin"), new Claim(InternalClaimTypes.InternalUserId, Guid.NewGuid().ToString())],
            notBefore: fault == "future" ? now.AddMinutes(10) : now.AddMinutes(-10),
            expires: fault == "expired" ? now.AddMinutes(-5) : now.AddMinutes(20),
            signingCredentials: fault == "unsigned" ? null : new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code, string? secret = null)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("CANARY", body);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
        if (secret is not null) Assert.DoesNotContain(secret, body + response.Headers);
        using var document = JsonDocument.Parse(body);
        Assert.Equal((int)status, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("traceId").GetString()));
    }

    private sealed class FailedAuthorization : IUserAuthorizationService
    {
        public Task<UserAuthorization?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("CANARY_PERMISSION_FAILURE");
    }
}
