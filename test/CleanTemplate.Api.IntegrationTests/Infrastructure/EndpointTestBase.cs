using System.Net;
using System.Net.Http.Json;

namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

public abstract class EndpointTestBase(TestWebApplicationFactory factory)
{
    protected const string SeedAdminPassword = TestWebApplicationFactory.AdminPassword;
    protected TestWebApplicationFactory Factory { get; } = factory;

    protected static async Task<LoginResponse> LoginAsync(HttpClient client)
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

    protected HttpClient CreateClient(string forwardedFor)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        return client;
    }

    protected sealed record CreatedProductResponse
    {
        public Guid Id { get; init; }
    }

    protected sealed record LoginResponse
    {
        public string AccessToken { get; init; } = string.Empty;
        public string RefreshToken { get; init; } = string.Empty;
        public string TokenType { get; init; } = string.Empty;
    }
}
