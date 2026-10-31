using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CleanTemplate.Api.IntegrationTests.Infrastructure;
using CleanTemplate.Application.Contracts;
using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Api.IntegrationTests.Security;

[Collection(HostIntegrationCollection.Name)]
public sealed class ProductAuthorizationTests
{
    [Fact]
    public async Task UserCanReadButCannotCreateUpdateOrDeleteProducts()
    {
        using var baseFactory = new TestWebApplicationFactory();
        var database = $"Data Source=product-authorization-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database,
                ["ConnectionStrings:ReadConnection"] = database,
                ["OpenTelemetry:Traces:Enabled"] = "false",
                ["OpenTelemetry:Metrics:Enabled"] = "false"
            })));
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "regular-user", Email = "user@example.test" };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, "User")).Succeeded);
        var generator = scope.ServiceProvider.GetRequiredService<ILocalJwtTokenGenerator>();
        var token = generator.Generate(new AuthUser { Id = user.Id, Email = user.Email }, (await users.GetRolesAsync(user)).ToArray());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var payload = new { Name = "Forbidden write", Price = 1m, Stock = 1 };

        using var read = await client.GetAsync("/api/products/");
        using var create = await client.PostAsJsonAsync("/api/products/", payload);
        using var update = await client.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}", payload);
        using var delete = await client.DeleteAsync($"/api/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }
}
