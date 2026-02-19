using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CleanTemplate.Api.IntegrationTests.Infrastructure;

namespace CleanTemplate.Api.IntegrationTests.Endpoints;

public sealed class AuthAndProductsEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private const string SeedAdminPassword = "ChangeMe_UseUserSecrets!123";

    private readonly TestWebApplicationFactory _factory;

    public AuthAndProductsEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WithSeededAdminCredentials_ReturnsAccessAndRefreshTokens()
    {
        using var client = CreateClient("198.51.100.10");

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            Email = "admin@local.template",
            Password = SeedAdminPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(payload.RefreshToken));
        Assert.Equal("Bearer", payload.TokenType);
    }

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
    public async Task Refresh_WithValidRefreshToken_ReturnsNewTokens()
    {
        using var client = CreateClient("198.51.100.13");
        var login = await LoginAsync(client);

        var refreshResponse = await client.PostAsJsonAsync("/auth/refresh", new
        {
            RefreshToken = login.RefreshToken
        });

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        var refreshPayload = await refreshResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(refreshPayload);
        Assert.False(string.IsNullOrWhiteSpace(refreshPayload.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshPayload.RefreshToken));
        Assert.NotEqual(login.RefreshToken, refreshPayload.RefreshToken);
    }

    [Fact]
    public async Task Logout_WithRefreshToken_RevokesToken()
    {
        using var client = CreateClient("198.51.100.14");
        var login = await LoginAsync(client);

        var logoutResponse = await client.PostAsJsonAsync("/auth/logout", new
        {
            RefreshToken = login.RefreshToken
        });

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var refreshResponse = await client.PostAsJsonAsync("/auth/refresh", new
        {
            RefreshToken = login.RefreshToken
        });

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
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
            Stock = 4
        });

        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync($"/api/products/{createdPayload.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var deleteMissingResponse = await client.DeleteAsync($"/api/products/{createdPayload.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteMissingResponse.StatusCode);
    }

    [Fact]
    public async Task Login_WhenRateLimitExceeded_ReturnsTooManyRequests()
    {
        using var client = CreateClient("198.51.100.42");

        HttpStatusCode lastStatusCode = HttpStatusCode.OK;
        for (var i = 0; i < 12; i++)
        {
            var response = await client.PostAsJsonAsync("/auth/login", new
            {
                Email = "admin@local.template",
                Password = SeedAdminPassword
            });

            lastStatusCode = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, lastStatusCode);
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client)
    {
        var loginResponse = await client.PostAsJsonAsync("/auth/login", new
        {
            Email = "admin@local.template",
            Password = SeedAdminPassword
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var payload = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(payload);
        return payload;
    }

    private HttpClient CreateClient(string forwardedFor)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        return client;
    }

    private sealed record CreatedProductResponse
    {
        public Guid Id { get; init; }
    }

    private sealed record LoginResponse
    {
        public string AccessToken { get; init; } = string.Empty;
        public string RefreshToken { get; init; } = string.Empty;
        public string TokenType { get; init; } = string.Empty;
    }
}
