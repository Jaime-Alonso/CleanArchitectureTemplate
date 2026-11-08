using System.Net;
using System.Net.Http.Json;
using CleanTemplate.Api.IntegrationTests.Infrastructure;

namespace CleanTemplate.Api.IntegrationTests.Endpoints;

[Collection(HostIntegrationCollection.Name)]
public sealed class LoginEndpointsTests(TestWebApplicationFactory factory) : EndpointTestBase(factory), IClassFixture<TestWebApplicationFactory>
{
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

}
