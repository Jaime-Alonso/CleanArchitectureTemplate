using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CleanTemplate.Api.IntegrationTests.Infrastructure;
using CleanTemplate.Application.Contracts.Identity;
using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Api.IntegrationTests.Security;

[Collection(HostIntegrationCollection.Name)]
public sealed class ProductAuthorizationTests
{
    [Fact]
    public async Task CreatedProductIsImmediatelyReadableOnPrimaryAndExtremePageIsEmpty()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "read-admin", Email = "read-admin@example.test" };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
        var generator = scope.ServiceProvider.GetRequiredService<ILocalJwtTokenGenerator>();
        var token = generator.Generate(new AuthUser { Id = user.Id, Email = user.Email }, (await users.GetRolesAsync(user)).ToArray());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var created = await client.PostAsJsonAsync("/api/products/", new { Name = "Immediate", Price = 1m, Stock = 1 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var read = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Contains("Immediate", await read.Content.ReadAsStringAsync());
        using var page = await client.GetAsync("/api/products/?page=2147483647&pageSize=2147483647");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var json = System.Text.Json.JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task UserCanReadButCannotCreateUpdateOrDeleteProducts()
    {
        using var factory = new TestWebApplicationFactory();
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
