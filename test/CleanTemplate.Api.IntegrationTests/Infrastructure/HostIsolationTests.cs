using System.Net;
using System.Net.Http.Headers;
using CleanTemplate.Application.Contracts.Identity;
using CleanTemplate.Application.Security;
using CleanTemplate.Domain.Entities;
using CleanTemplate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

[Collection(HostIntegrationCollection.Name)]
public sealed class HostIsolationTests
{
    [Fact]
    public async Task TestingHosts_IsolateConfigurationDataAndSigningKeys()
    {
        await using var first = new TestWebApplicationFactory();
        await using var second = new TestWebApplicationFactory();
        using var firstClient = first.CreateClient();
        using var secondClient = second.CreateClient();
        using var firstScope = first.Services.CreateScope();
        using var secondScope = second.Services.CreateScope();
        var firstServices = firstScope.ServiceProvider;
        var secondServices = secondScope.ServiceProvider;
        foreach (var services in new[] { firstServices, secondServices })
        {
            Assert.Equal("Testing", services.GetRequiredService<IHostEnvironment>().EnvironmentName);
            var configuration = (IConfigurationRoot)services.GetRequiredService<IConfiguration>();
            Assert.NotEmpty(configuration.Providers);
            Assert.All(configuration.Providers, provider => Assert.IsType<MemoryConfigurationProvider>(provider));
        }
        var firstDb = firstServices.GetRequiredService<ApplicationDbContext>();
        var secondDb = secondServices.GetRequiredService<ApplicationDbContext>();
        Assert.True(firstDb.Database.IsSqlite());
        Assert.True(secondDb.Database.IsSqlite());
        Assert.NotEqual(firstDb.Database.GetConnectionString(), secondDb.Database.GetConnectionString());
        // Data and bearer credentials must remain independent while both hosts are alive.
        var product = new Product("first-host-only", null, 1m, 1);
        firstDb.Add(product);
        await firstDb.SaveChangesAsync();
        Assert.False(await secondDb.Set<Product>().AnyAsync());
        var admin = await firstDb.Users.SingleAsync();
        var token = firstServices.GetRequiredService<ILocalJwtTokenGenerator>()
            .Generate(new AuthUser { Id = admin.Id, Email = admin.Email }, ["Admin"]).AccessToken;
        foreach (var client in new[] { firstClient, secondClient })
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var accepted = await firstClient.GetAsync($"/api/products/{product.Id}");
        using var rejected = await secondClient.GetAsync($"/api/products/{product.Id}");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [Fact]
    public async Task ProductionEphemeralOptIn_CreatesSchemaWithoutBootstrapOrDevelopmentRoutes()
    {
        await using var factory = new ProductionWebApplicationFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        Assert.Equal("Production", scope.ServiceProvider.GetRequiredService<IHostEnvironment>().EnvironmentName);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Users.AnyAsync());
        Assert.False(await db.Roles.AnyAsync());
        Assert.Null(scope.ServiceProvider.GetRequiredService<IConfiguration>()["IdentitySeed:AdminPassword"]);
        foreach (var path in new[] { "/health/live", "/health/ready" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        foreach (var path in new[] { "/openapi/v1.json", "/openapi/v2.json", "/scalar", "/scalar/v1" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var protectedResponse = await client.GetAsync("/api/products/");
        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
    }

    [Fact]
    public async Task Staging_DoesNotInitializeSchema_AndReadinessOnlyChecksConnectivity()
    {
        await using var factory = new TestWebApplicationFactory("Staging");
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        Assert.Equal("Staging", scope.ServiceProvider.GetRequiredService<IHostEnvironment>().EnvironmentName);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('AspNetUsers', 'Products')";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        using var documentation = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.NotFound, documentation.StatusCode);
    }
}
