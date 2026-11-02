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
    public async Task OpenApi_CuratedProductAndProblemContract()
    {
        using var client = factory.CreateClient();
        using var document = await client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json");
        var root = document!.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");

        static void Required(JsonElement schema, params string[] names)
        {
            var required = schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();
            foreach (var name in names) Assert.Contains(name, required);
        }

        static void Property(JsonElement schema, string name, string type, string? format = null)
        {
            var property = schema.GetProperty("properties").GetProperty(name);
            Assert.Equal(type, property.GetProperty("type").GetString());
            if (format is not null) Assert.Equal(format, property.GetProperty("format").GetString());
        }

        foreach (var schemaName in new[] { "CreateProductRequest", "UpdateProductRequest", "ProductDto", "ProductListItemDto" })
        {
            var schema = schemas.GetProperty(schemaName);
            var properties = schema.GetProperty("properties");
            Property(schema, "price", "number", "decimal");
            Assert.Equal(0m, properties.GetProperty("price").GetProperty("minimum").GetDecimal());
            Assert.Equal(9999999999999999.99m, properties.GetProperty("price").GetProperty("maximum").GetDecimal());
            Assert.Equal(0.01m, properties.GetProperty("price").GetProperty("multipleOf").GetDecimal());
            Assert.Equal(200, properties.GetProperty("name").GetProperty("maxLength").GetInt32());
            Assert.Equal(1, properties.GetProperty("name").GetProperty("minLength").GetInt32());
            Assert.Equal(0, properties.GetProperty("stock").GetProperty("minimum").GetInt32());
            if (properties.TryGetProperty("description", out var description))
            {
                Assert.Equal(1000, description.GetProperty("maxLength").GetInt32());
                var types = description.GetProperty("type").EnumerateArray().Select(value => value.GetString());
                Assert.Contains("string", types);
                Assert.Contains("null", types);
            }
            if (schemaName.EndsWith("Dto", StringComparison.Ordinal))
            {
                Required(schema, "id", "version", "price");
                Property(schema, "id", "string", "uuid");
                Property(schema, "version", "integer", "int64");
                Assert.Equal(1, properties.GetProperty("version").GetProperty("minimum").GetInt32());
            }
        }
        var update = schemas.GetProperty("UpdateProductRequest");
        Required(update, "expectedVersion", "name");
        Property(update, "expectedVersion", "integer", "int64");
        Assert.Equal(1, update.GetProperty("properties").GetProperty("expectedVersion").GetProperty("minimum").GetInt32());
        foreach (var name in new[] { "CreateProductRequest", "UpdateProductRequest" })
        {
            var required = schemas.GetProperty(name).GetProperty("required").EnumerateArray().Select(value => value.GetString());
            Assert.DoesNotContain("price", required);
            Assert.DoesNotContain("stock", required);
        }
        var created = schemas.GetProperty("CreateProductResponse");
        Required(created, "id");
        Property(created, "id", "string", "uuid");
        foreach (var name in new[] { "ProblemDetails", "HttpValidationProblemDetails" })
        {
            var schema = schemas.GetProperty(name);
            Required(schema, "code", "traceId");
            Property(schema, "code", "string");
            Property(schema, "traceId", "string");
            if (name == "HttpValidationProblemDetails")
            {
                Required(schema, "errors");
                Property(schema, "errors", "object");
                var values = schema.GetProperty("properties").GetProperty("errors").GetProperty("additionalProperties");
                Assert.Equal("array", values.GetProperty("type").GetString());
                Assert.Equal("string", values.GetProperty("items").GetProperty("type").GetString());
            }
        }
        var paths = root.GetProperty("paths");
        var delete = paths.GetProperty("/api/products/{id}").GetProperty("delete");
        var expectedVersion = delete.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "expectedVersion");
        Assert.True(expectedVersion.GetProperty("required").GetBoolean());
        var parameterSchema = expectedVersion.GetProperty("schema");
        Assert.Equal("integer", parameterSchema.GetProperty("type").GetString());
        Assert.Equal("int64", parameterSchema.GetProperty("format").GetString());
        Assert.Equal(1, parameterSchema.GetProperty("minimum").GetInt32());
        var badRequest = delete.GetProperty("responses").GetProperty("400").GetProperty("content").GetProperty("application/problem+json").GetProperty("schema");
        var alternatives = badRequest.GetProperty("anyOf").EnumerateArray().ToArray();
        Assert.Contains(alternatives, alternative => alternative.GetProperty("properties").TryGetProperty("errors", out _));
        Assert.Contains(alternatives, alternative => !alternative.GetProperty("properties").TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task OpenApi_DeclaresSuccessAndConcurrencyResponses()
    {
        using var client = factory.CreateClient();
        using var document = await client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json");
        var bearer = document!.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var schemaName in new[] { "ProblemDetails", "HttpValidationProblemDetails" })
        {
            var properties = schemas.GetProperty(schemaName).GetProperty("properties");
            Assert.Equal("string", properties.GetProperty("code").GetProperty("type").GetString());
            Assert.Equal("string", properties.GetProperty("traceId").GetProperty("type").GetString());
            if (schemaName == "HttpValidationProblemDetails") Assert.True(properties.TryGetProperty("errors", out _));
        }
        var paths = document!.RootElement.GetProperty("paths");
        foreach (var (path, method, status) in new[]
        {
            ("/auth/login", "post", "200"), ("/auth/refresh", "post", "200"),
            ("/auth/logout", "post", "204"), ("/api/products", "post", "201"),
            ("/api/products", "get", "200"), ("/api/products/{id}", "get", "200"),
            ("/api/products/{id}", "put", "204"), ("/api/products/{id}", "delete", "204")
        })
        {
            var operation = paths.GetProperty(path).GetProperty(method);
            if (path.StartsWith("/api/products", StringComparison.Ordinal))
                Assert.Contains(operation.GetProperty("security").EnumerateArray(), s => s.TryGetProperty("Bearer", out _));
            else
                Assert.False(operation.TryGetProperty("security", out _));
            var responses = operation.GetProperty("responses");
            Assert.True(responses.GetProperty("429").GetProperty("headers").TryGetProperty("Retry-After", out _));
            if (responses.TryGetProperty("401", out var unauthorized))
                Assert.True(unauthorized.GetProperty("headers").TryGetProperty("WWW-Authenticate", out _));
            var success = responses.GetProperty(status);
            if (status == "204") Assert.False(success.TryGetProperty("content", out _));
            else Assert.True(success.GetProperty("content").GetProperty("application/json").TryGetProperty("schema", out _));
            if (method is "put" or "delete") Assert.True(responses.TryGetProperty("409", out _));
        }
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
