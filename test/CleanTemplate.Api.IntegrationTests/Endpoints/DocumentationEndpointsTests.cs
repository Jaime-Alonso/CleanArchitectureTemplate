using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Asp.Versioning;
using CleanTemplate.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Api.IntegrationTests.Endpoints;

[Collection(HostIntegrationCollection.Name)]
public sealed class DocumentationEndpointsTests(DevelopmentWebApplicationFactory factory) : EndpointTestBase(factory), IClassFixture<DevelopmentWebApplicationFactory>
{
    [Fact]
    public async Task GetOpenApi_ForApiVersion_ReturnsMatchingDocument()
    {
        using var client = CreateClient("198.51.100.20");
        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(document);
        Assert.Equal("v1", document.RootElement.GetProperty("info").GetProperty("version").GetString());
        Assert.NotEmpty(document.RootElement.GetProperty("paths").EnumerateObject());
    }

    [Fact]
    public async Task GetOpenApi_ForUnregisteredApiVersion_ReturnsNotFound()
    {
        using var client = CreateClient("198.51.100.21");
        using var response = await client.GetAsync("/openapi/v2.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HealthyEndpoints_AreExcludedFromOpenApi()
    {
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/health/live", "/health/ready", "/health" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        }
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        Assert.DoesNotContain(document.RootElement.GetProperty("paths").EnumerateObject(), path => path.Name.StartsWith("/health", StringComparison.Ordinal));
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/health", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(3, endpoints.Length);
        Assert.All(endpoints, endpoint =>
        {
            Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.NotNull(endpoint.Metadata.GetMetadata<DisableRateLimitingAttribute>());
            Assert.True(endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription);
            Assert.NotNull(endpoint.Metadata.GetMetadata<ApiVersionNeutralAttribute>());
        });
    }

}
