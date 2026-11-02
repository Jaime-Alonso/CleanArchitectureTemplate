using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Asp.Versioning;
using CleanTemplate.Api.Extensions;
using CleanTemplate.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Api.IntegrationTests.Endpoints;

[Collection(HostIntegrationCollection.Name)]
public sealed class ApiVersionTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Theory]
    [InlineData(null)]
    [InlineData("1.0")]
    public async Task Login_AcceptsDefaultAndExplicitVersion(string? version)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login");
        if (version is not null) request.Headers.Add("X-Api-Version", version);
        request.Content = JsonContent.Create(new { Email = "nobody@example.com", Password = "invalid" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("1.0", response.Headers.GetValues("api-supported-versions"));
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("invalid")]
    [InlineData("1.0, 2.0")]
    public async Task Login_RejectsInvalidVersionsAsProblemDetails(string version)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login");
        request.Headers.TryAddWithoutValidation("X-Api-Version", version);
        request.Headers.TryAddWithoutValidation("Accept", "text/plain");
        request.Content = JsonContent.Create(new { Email = "nobody@example.com", Password = "invalid" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("request.invalid", json!.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("nobody@example.com", json.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenApi_ContainsVersionedBusinessOperationsAndHeader()
    {
        using var client = factory.CreateClient();
        using var json = await client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json");
        var paths = json!.RootElement.GetProperty("paths");
        foreach (var (path, methods) in new[]
        {
            ("/auth/login", new[] { "post" }), ("/auth/refresh", new[] { "post" }),
            ("/auth/logout", new[] { "post" }), ("/api/products", new[] { "get", "post" }),
            ("/api/products/{id}", new[] { "get", "put", "delete" })
        })
        {
            foreach (var method in methods)
            {
                var operation = paths.GetProperty(path).GetProperty(method);
                Assert.Contains(operation.GetProperty("parameters").EnumerateArray(), p =>
                    p.GetProperty("name").GetString() == "X-Api-Version" && p.GetProperty("in").GetString() == "header" &&
                    (!p.TryGetProperty("required", out var required) || !required.GetBoolean()));
            }
        }
        Assert.False(paths.TryGetProperty("/health", out _));
    }

    [Fact]
    public void BusinessEndpoints_DeclareVersionOneAndHealthIsNeutral()
    {
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        var business = endpoints.OfType<RouteEndpoint>().Where(e =>
            e.RoutePattern.RawText!.StartsWith("/auth/", StringComparison.Ordinal) ||
            e.RoutePattern.RawText.StartsWith("/api/products", StringComparison.Ordinal)).ToArray();
        Assert.Equal(8, business.Length);
        foreach (var endpoint in business)
        {
            var metadata = endpoint.Metadata.GetMetadata<ApiVersionMetadata>();
            Assert.NotNull(metadata);
            var model = metadata.Map(ApiVersionMapping.Explicit | ApiVersionMapping.Implicit);
            Assert.Contains(new ApiVersion(1, 0), model.DeclaredApiVersions);
            Assert.Empty(model.DeprecatedApiVersions);
        }
        var health = endpoints.OfType<RouteEndpoint>().Single(e => e.RoutePattern.RawText == "/health");
        Assert.Null(health.Metadata.GetMetadata<ApiVersionMetadata>());
    }

    [Fact]
    public async Task MultipleHeaderValuesAreRejected()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login");
        request.Headers.TryAddWithoutValidation("X-Api-Version", new[] { "1.0", "2.0" });
        request.Content = JsonContent.Create(new { Email = "nobody@example.com", Password = "invalid" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("request.invalid", json!.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task TechnicalRoutesIgnoreVersionAndUnknownProductVersionPrecedesAuthorization()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Version", "2.0");
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using var document = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        using var product = await client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.BadRequest, product.StatusCode);
    }

    [Fact]
    public async Task IsolatedDeprecatedVersion_ReportsDeprecation()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiVersioningConfiguration();
        builder.Services.AddGlobalErrorHandling();
        await using var app = builder.Build();
        app.UseGlobalErrorHandling();
        var versions = app.NewApiVersionSet().HasDeprecatedApiVersion(new ApiVersion(1, 0)).ReportApiVersions().Build();
        app.MapGet("/example", () => Results.Ok()).WithApiVersionSet(versions).MapToApiVersion(1.0);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Version", "1.0");
        using var response = await client.GetAsync("/example");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("1.0", response.Headers.GetValues("api-deprecated-versions"));
        var endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/example");
        Assert.Contains(new ApiVersion(1, 0), endpoint.Metadata.GetMetadata<ApiVersionMetadata>()!
            .Map(ApiVersionMapping.Explicit | ApiVersionMapping.Implicit).DeprecatedApiVersions);
    }
}
