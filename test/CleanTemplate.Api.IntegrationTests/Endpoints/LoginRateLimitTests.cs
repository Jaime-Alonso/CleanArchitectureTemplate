using System.Net;
using System.Net.Http.Json;
using CleanTemplate.Api.IntegrationTests.Infrastructure;

namespace CleanTemplate.Api.IntegrationTests.Endpoints;

[Collection(HostIntegrationCollection.Name)]
public sealed class LoginRateLimitTests(TestWebApplicationFactory factory) : EndpointTestBase(factory), IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Login_WhenRateLimitExceeded_ReturnsTooManyRequests()
    {
        using var client = CreateClient("198.51.100.42");

        for (var i = 0; i < 5; i++)
        {
            using var response = await client.PostAsJsonAsync("/auth/login", new
            {
                Email = "admin@local.template",
                Password = SeedAdminPassword
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var rejected = await client.PostAsJsonAsync("/auth/login", new
        {
            Email = "admin@local.template",
            Password = SeedAdminPassword
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"));
    }

}
