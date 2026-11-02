using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanTemplate.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace CleanTemplate.Api.IntegrationTests.Security;

[Collection(HostIntegrationCollection.Name)]
public sealed class DisabledLocalAuthEndpointsTests
{
    [Fact]
    public async Task LocalJwtDisabled_DoesNotMapLocalAuthEndpoints()
    {
        using var baseFactory = new TestWebApplicationFactory();
        var database = $"Data Source=disabled-auth-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Schemes:LocalJwt:Enabled"] = "false",
                ["Auth:Schemes:LocalJwt:SigningKey"] = "",
                ["Auth:Schemes:Oidc:Enabled"] = "true",
                ["Auth:Schemes:Oidc:Authority"] = "https://identity.test",
                ["Auth:Schemes:Oidc:Audience"] = "security-tests",
                ["ConnectionStrings:DefaultConnection"] = database,
                ["ConnectionStrings:ReadConnection"] = database,
                ["OpenTelemetry:Traces:Enabled"] = "false",
                ["OpenTelemetry:Metrics:Enabled"] = "false"
            })));
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/auth/login", "/auth/refresh", "/auth/logout" })
        {
            using var response = await client.PostAsJsonAsync(path, new { RefreshToken = "not-issued", Email = "unused", Password = "unused" });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var document = await client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json");
        var paths = document!.RootElement.GetProperty("paths");
        Assert.False(paths.TryGetProperty("/auth/login", out _));
        Assert.False(paths.TryGetProperty("/auth/refresh", out _));
        Assert.False(paths.TryGetProperty("/auth/logout", out _));
    }
}
