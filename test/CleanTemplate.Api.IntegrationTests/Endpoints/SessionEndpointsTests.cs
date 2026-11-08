using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CleanTemplate.Api.IntegrationTests.Infrastructure;

namespace CleanTemplate.Api.IntegrationTests.Endpoints;

[Collection(HostIntegrationCollection.Name)]
public sealed class SessionEndpointsTests(TestWebApplicationFactory factory) : EndpointTestBase(factory), IClassFixture<TestWebApplicationFactory>
{
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
    public async Task Refresh_WithConsumedToken_ReturnsUnauthorizedAndRevokesReplacement()
    {
        using var client = CreateClient("198.51.100.16");
        var login = await LoginAsync(client);
        using var first = await client.PostAsJsonAsync("/auth/refresh", new { login.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var replacement = await first.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(replacement);

        using var replay = await client.PostAsJsonAsync("/auth/refresh", new { login.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // A replay revokes the entire family, including the already-issued replacement.
        using var next = await client.PostAsJsonAsync("/auth/refresh", new { replacement.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, next.StatusCode);
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
    public async Task Logout_WithConsumedAncestor_RevokesDescendantButNotIssuedAccessToken()
    {
        using var client = CreateClient("198.51.100.17");
        var login = await LoginAsync(client);
        using var refresh = await client.PostAsJsonAsync("/auth/refresh", new { login.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var child = await refresh.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(child);

        using var logout = await client.PostAsJsonAsync("/auth/logout", new { login.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var rejected = await client.PostAsJsonAsync("/auth/refresh", new { child.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", child.AccessToken);
        using var products = await client.GetAsync("/api/products?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
    }

}
