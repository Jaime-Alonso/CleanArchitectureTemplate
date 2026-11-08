using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CleanTemplate.Api.IntegrationTests.Infrastructure;
using CleanTemplate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Api.IntegrationTests.Endpoints;

[Collection(HostIntegrationCollection.Name)]
public sealed class ProductEndpointsTests(TestWebApplicationFactory factory) : EndpointTestBase(factory), IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task GetProducts_WithoutToken_ReturnsUnauthorized()
    {
        using var client = CreateClient("198.51.100.11");

        var response = await client.GetAsync("/api/products?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProducts_WithValidToken_ReturnsOk()
    {
        using var client = CreateClient("198.51.100.12");

        var loginResponse = await client.PostAsJsonAsync("/auth/login", new
        {
            Email = "admin@local.template",
            Password = SeedAdminPassword
        });

        var payload = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(payload);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.AccessToken);

        var productsResponse = await client.GetAsync("/api/products?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, productsResponse.StatusCode);
    }

    [Fact]
    public async Task Products_InvalidValuesReturnBadRequestWithoutChangingStoredProduct()
    {
        using var client = CreateClient("198.51.100.89");
        var login = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var description = string.Concat(Enumerable.Repeat("😀", 500));
        using var maximum = await client.PostAsJsonAsync("/api/products/", new { Name = "maximum", Description = description, Price = 9999999999999999.99m, Stock = 1 });
        Assert.Equal(HttpStatusCode.Created, maximum.StatusCode);
        using var create = await client.PostAsJsonAsync("/api/products/", new { Name = "boundary", Description = description, Price = 1.000m, Stock = 1 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<CreatedProductResponse>())!.Id;
        var url = $"/api/products/{id}";
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var count = await db.Set<CleanTemplate.Domain.Entities.Product>().CountAsync();
        foreach (var (text, price, stock) in new[]
                 {
                     (description + "x", 1m, 1),
                     (description, 9999999999999999.99m + 0.01m, 1),
                     (description, 1.001m, 1),
                     (description, -0.01m, 1),
                     (description, 1m, -1)
                 })
        {
            using var invalidCreate = await client.PostAsJsonAsync("/api/products/", new { Name = "invalid", Description = text, Price = price, Stock = stock });
            Assert.Equal(HttpStatusCode.BadRequest, invalidCreate.StatusCode);
            using var invalidUpdate = await client.PutAsJsonAsync(url, new { Name = "changed", Description = text, Price = price, Stock = stock, ExpectedVersion = 1 });
            Assert.Equal(HttpStatusCode.BadRequest, invalidUpdate.StatusCode);
            using var read = await client.GetAsync(url);
            var saved = await read.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("boundary", saved.GetProperty("name").GetString());
            Assert.Equal(description, saved.GetProperty("description").GetString());
            Assert.Equal(1m, saved.GetProperty("price").GetDecimal());
            Assert.Equal(1, saved.GetProperty("stock").GetInt32());
            Assert.Equal(1, saved.GetProperty("version").GetInt64());
            Assert.Equal(count, await db.Set<CleanTemplate.Domain.Entities.Product>().CountAsync());
            var stored = await db.Set<CleanTemplate.Domain.Entities.Product>().AsNoTracking().SingleAsync(p => p.Id == id);
            Assert.Equal("boundary", stored.Name);
            Assert.Equal(description, stored.Description);
            Assert.Equal(1m, stored.Price);
            Assert.Equal(1, stored.Stock);
            Assert.Equal(1, stored.Version);
        }
    }

    [Fact]
    public async Task Products_AdminCrudFlow_WorksEndToEnd()
    {
        using var client = CreateClient("198.51.100.15");
        var login = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/products/", new
        {
            Name = "Integration Product",
            Description = "Created by integration test",
            Price = 10.5m,
            Stock = 2
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdPayload = await createResponse.Content.ReadFromJsonAsync<CreatedProductResponse>();
        Assert.NotNull(createdPayload);
        Assert.NotEqual(Guid.Empty, createdPayload.Id);

        var getByIdResponse = await client.GetAsync($"/api/products/{createdPayload.Id}");
        if (getByIdResponse.StatusCode != HttpStatusCode.OK)
        {
            var body = await getByIdResponse.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException($"Expected 200 for get by id but got {(int)getByIdResponse.StatusCode}. Body: {body}");
        }

        var updateResponse = await client.PutAsJsonAsync($"/api/products/{createdPayload.Id}", new
        {
            Name = "Integration Product Updated",
            Description = "Updated",
            Price = 12m,
            Stock = 4,
            ExpectedVersion = 1
        });

        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync($"/api/products/{createdPayload.Id}?expectedVersion=2");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var deleteMissingResponse = await client.DeleteAsync($"/api/products/{createdPayload.Id}?expectedVersion=2");
        Assert.Equal(HttpStatusCode.NotFound, deleteMissingResponse.StatusCode);
    }

    [Fact]
    public async Task Products_StaleClientsCannotUpdateOrDeleteNewerVersion()
    {
        using var first = CreateClient("198.51.100.21");
        using var second = CreateClient("198.51.100.22");
        var login = await LoginAsync(first);
        first.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        second.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var create = await first.PostAsJsonAsync("/api/products/", new { Name = "concurrent", Price = 1m, Stock = 1 });
        var created = (await create.Content.ReadFromJsonAsync<CreatedProductResponse>())!;
        var url = $"/api/products/{created.Id}";
        using var missingVersion = await first.PutAsJsonAsync(url, new { Name = "invalid", Price = 1m, Stock = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, missingVersion.StatusCode);
        using var invalidDelete = await first.DeleteAsync($"{url}?expectedVersion=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalidDelete.StatusCode);
        using var read1 = await first.GetAsync(url);
        using var read2 = await second.GetAsync(url);
        var version1 = (await read1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt64();
        var version2 = (await read2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt64();
        Assert.Equal(version1, version2);
        using var winner = await first.PutAsJsonAsync(url, new { Name = "winner", Price = 2m, Stock = 2, ExpectedVersion = version1 });
        using var loser = await second.PutAsJsonAsync(url, new { Name = "loser", Price = 3m, Stock = 3, ExpectedVersion = version2 });
        Assert.Equal(HttpStatusCode.NoContent, winner.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
        using var staleDelete = await second.DeleteAsync($"{url}?expectedVersion={version2}");
        Assert.Equal(HttpStatusCode.Conflict, staleDelete.StatusCode);
        using var read = await first.GetAsync(url);
        var product = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("winner", product.GetProperty("name").GetString());
        Assert.Equal(version1 + 1, product.GetProperty("version").GetInt64());
    }

}
