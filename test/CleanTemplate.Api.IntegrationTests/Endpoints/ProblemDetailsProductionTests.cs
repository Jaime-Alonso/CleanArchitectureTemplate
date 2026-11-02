using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CleanTemplate.Application.Common.Pagination;
using CleanTemplate.Application.Contracts;
using CleanTemplate.Application.Products.Queries.GetProductById;
using CleanTemplate.Application.Products.ReadModels;
using CleanTemplate.Domain.Exceptions;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
using Mediora;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CleanTemplate.Api.IntegrationTests.Endpoints;

[Collection("Host integration")]
public sealed class ProblemDetailsProductionTests
{
    private const string Secret = "sensitive-injected-secret";

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/html")]
    public async Task ErrorsIgnoreAcceptAndUnauthorizedResponsesIncludeBearer(string accept)
    {
        using var settings = new EnvironmentSettings();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Accept.ParseAdd(accept);
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>().SeedAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = (await users.FindByEmailAsync("admin@local.template"))!;
        var generator = scope.ServiceProvider.GetRequiredService<ILocalJwtTokenGenerator>();

        using (var login = await client.PostAsJsonAsync("/auth/login", new { Email = "nobody", Password = "wrong" }))
        {
            using var body = await Check(login, HttpStatusCode.Unauthorized, "auth.unauthorized");
            Assert.Contains(login.Headers.WwwAuthenticate, h => h.Scheme == "Bearer" && h.Parameter is null);
        }
        using (var refresh = await client.PostAsJsonAsync("/auth/refresh", new { RefreshToken = Secret }))
        {
            using var body = await Check(refresh, HttpStatusCode.Unauthorized, "auth.unauthorized");
            Assert.Contains(refresh.Headers.WwwAuthenticate, h => h.Scheme == "Bearer" && h.Parameter is null);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", generator.Generate(
            new CleanTemplate.Application.Security.AuthUser { Id = admin.Id, Email = admin.Email! }, ["Admin"]).AccessToken);
        using (var missing = await client.GetAsync($"/api/products/{Guid.Empty}"))
        using (await Check(missing, HttpStatusCode.NotFound, "resource.not_found")) { }
        using (var invalid = await client.GetAsync("/api/products/00000000-0000-0000-0000-000000000006"))
        using (await Check(invalid, HttpStatusCode.BadRequest, "request.invalid")) { }
        using (var unauthorized = await client.GetAsync("/api/products/00000000-0000-0000-0000-000000000007"))
        {
            using var body = await Check(unauthorized, HttpStatusCode.Unauthorized, "auth.unauthorized");
            Assert.Contains(unauthorized.Headers.WwwAuthenticate, h => h.Scheme == "Bearer" && h.Parameter is null);
        }
        using (var created = await client.PostAsJsonAsync("/api/products/", new { Name = "test", Price = 1, Stock = 1 }))
        {
            using var conflict = await client.DeleteAsync($"{created.Headers.Location}?expectedVersion=2");
            using var body = await Check(conflict, HttpStatusCode.Conflict, "resource.conflict");
        }
        for (var i = 0; i < 2; i++)
        {
            using var response = await client.PostAsJsonAsync("/auth/login", new { Email = "nobody", Password = "wrong" });
            if (i == 1)
            {
                using var body = await Check(response, HttpStatusCode.TooManyRequests, "rate_limit.exceeded");
                Assert.True(response.Headers.Contains("Retry-After"));
            }
        }
    }

    [Fact]
    public async Task DisabledLocalJwtKeepsEndpointsUnmappedAndProtectedProductsChallenged()
    {
        using var settings = new EnvironmentSettings(disableLocalJwt: true);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var missing = await client.PostAsJsonAsync("/auth/login", new { Email = "unused", Password = "unused" });
        await Check(missing, HttpStatusCode.NotFound, "resource.not_found");
        using var unauthorized = await client.GetAsync("/api/products/");
        await Check(unauthorized, HttpStatusCode.Unauthorized, "auth.unauthorized");
        Assert.NotEmpty(unauthorized.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task ProductionErrorsUseOneContractAndPreserveSecurityAndHeaders()
    {
        using var settings = new EnvironmentSettings();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>().SeedAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var regular = new ApplicationUser { Id = Guid.NewGuid(), UserName = "regular", Email = "regular@example.test" };
        Assert.True((await users.CreateAsync(regular)).Succeeded);
        Assert.True((await users.AddToRoleAsync(regular, "User")).Succeeded);
        var generator = scope.ServiceProvider.GetRequiredService<ILocalJwtTokenGenerator>();

        using (var unauthorized = await client.GetAsync("/api/products/"))
        {
            await Check(unauthorized, HttpStatusCode.Unauthorized, "auth.unauthorized");
            Assert.NotEmpty(unauthorized.Headers.WwwAuthenticate);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Secret);
        using (var invalidToken = await client.GetAsync("/api/products/"))
        {
            await Check(invalidToken, HttpStatusCode.Unauthorized, "auth.unauthorized");
            Assert.NotEmpty(invalidToken.Headers.WwwAuthenticate);
            Assert.DoesNotContain(Secret, invalidToken.Headers.WwwAuthenticate.ToString());
            Assert.DoesNotContain("error_description", invalidToken.Headers.WwwAuthenticate.ToString());
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", generator.Generate(
            new CleanTemplate.Application.Security.AuthUser { Id = regular.Id, Email = regular.Email }, ["User"]).AccessToken);
        using (var forbidden = await client.PostAsJsonAsync("/api/products/", new { Name = "x", Price = 1, Stock = 1 }))
            await Check(forbidden, HttpStatusCode.Forbidden, "auth.forbidden");

        var admin = (await users.FindByEmailAsync("admin@local.template"))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", generator.Generate(
            new CleanTemplate.Application.Security.AuthUser { Id = admin.Id, Email = admin.Email! }, ["Admin"]).AccessToken);
        using (var malformed = await client.PostAsync("/api/products/", new StringContent("{\"name\":", Encoding.UTF8, "application/json")))
        {
            using var body = await Check(malformed, HttpStatusCode.BadRequest, "request.invalid");
            Assert.True(body.RootElement.TryGetProperty("errors", out _));
        }
        using (var wrongType = await client.PostAsync("/api/products/", new StringContent("{\"price\":\"bad\"}", Encoding.UTF8, "application/json")))
        {
            using var body = await Check(wrongType, HttpStatusCode.BadRequest, "request.invalid");
            Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("price", out _));
        }
        using (var invalidFields = await client.PostAsJsonAsync("/api/products/", new { Name = "", Price = -1, Stock = -1 }))
        {
            using var body = await Check(invalidFields, HttpStatusCode.BadRequest, "request.invalid");
            Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("Name", out _), body.RootElement.ToString());
        }
        using (var invalid = await client.DeleteAsync($"/api/products/{Guid.NewGuid()}?expectedVersion=0"))
        {
            using var body = await Check(invalid, HttpStatusCode.BadRequest, "request.invalid");
            Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("expectedVersion", out _));
        }
        using (var invalid = await client.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}", new { ExpectedVersion = 0, Name = "x", Price = 1, Stock = 1 }))
        {
            using var body = await Check(invalid, HttpStatusCode.BadRequest, "request.invalid");
            Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("expectedVersion", out _));
        }
        using (var missing = await client.GetAsync($"/api/products/{Guid.Empty}"))
            await Check(missing, HttpStatusCode.NotFound, "resource.not_found");
        using (var created = await client.PostAsJsonAsync("/api/products/", new { Name = "test", Price = 1, Stock = 1 }))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var conflict = await client.DeleteAsync($"{created.Headers.Location}?expectedVersion=2");
            await Check(conflict, HttpStatusCode.Conflict, "resource.conflict");
        }
        foreach (var id in new[] { "00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000002", "00000000-0000-0000-0000-000000000003", "00000000-0000-0000-0000-000000000004" })
        {
            using var response = await client.GetAsync($"/api/products/{id}");
            await Check(response, id.EndsWith('4') ? HttpStatusCode.BadRequest : HttpStatusCode.InternalServerError,
                id.EndsWith('4') ? "request.invalid" : "server.error");
        }
        using (var failure = await client.GetAsync("/api/products/00000000-0000-0000-0000-000000000005"))
            await Check(failure, HttpStatusCode.InternalServerError, "server.error");
        using (var validation = await client.GetAsync("/api/products/00000000-0000-0000-0000-000000000006"))
        {
            using var body = await Check(validation, HttpStatusCode.BadRequest, "request.invalid");
            Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("Name", out _));
        }
        for (var i = 0; i < 3; i++)
        {
            using var response = await client.PostAsJsonAsync("/auth/login", new { Email = "nobody", Password = "wrong" });
            if (i == 2)
            {
                await Check(response, HttpStatusCode.TooManyRequests, "rate_limit.exceeded");
                Assert.True(response.Headers.Contains("Retry-After"));
            }
        }
    }

    private static WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Scoped<IProductReadRepository, InjectedReads>());
            services.Replace(ServiceDescriptor.Scoped<IRequestHandler<GetProductByIdQuery, Result<ProductDto>>, InjectedHandler>());
        });
    });

    private static async Task<JsonDocument> Check(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Secret, text);
        Assert.DoesNotContain("stackTrace", text, StringComparison.OrdinalIgnoreCase);
        var body = JsonDocument.Parse(text);
        Assert.Equal((int)status, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
        return body;
    }

    private sealed class InjectedReads : IProductReadRepository
    {
        public Task<ProductReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => id.ToString()[^1] switch
        {
            '1' => throw new InvalidOperationException(Secret),
            '2' => throw new NotSupportedException(Secret),
            '3' => throw new TimeoutException(Secret),
            '4' => throw new DomainValidationException("sensitive.code", Secret),
            _ => Task.FromResult<ProductReadModel?>(null)
        };

        public Task<PagedResult<ProductListItemReadModel>> GetPagedAsync(ProductSearchCriteria criteria, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class InjectedHandler(IProductReadRepository repository) : IRequestHandler<GetProductByIdQuery, Result<ProductDto>>
    {
        public Task<Result<ProductDto>> Handle(GetProductByIdQuery request, CancellationToken cancellationToken) => request.Id.ToString()[^1] switch
        {
            '5' => Task.FromResult(Result<ProductDto>.Failure(Error.Failure("Internal.Failure", Secret))),
            '6' => Task.FromResult(Result<ProductDto>.Failure(Error.Validation("Validation.Name", Secret))),
            '7' => Task.FromResult(Result<ProductDto>.Failure(new Error("Auth.Unauthorized", Secret, ErrorType.Unauthorized))),
            _ => new GetProductByIdQueryHandler(repository).Handle(request, cancellationToken)
        };
    }

    private sealed class EnvironmentSettings : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new();

        public EnvironmentSettings(bool disableLocalJwt = false)
        {
            Set("DatabaseProvider", "SqliteInMemory");
            Set("ConnectionStrings__DefaultConnection", $"Data Source=problems-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            Set("Auth__Schemes__LocalJwt__SigningKey", "production-test-signing-key-1234567890");
            Set("IdentitySeed__AdminPassword", "ChangeMe_UseUserSecrets!123");
            Set("Auth__RefreshTokenCleanup__Enabled", "false");
            Set("OpenTelemetry__Traces__Enabled", "false");
            Set("OpenTelemetry__Metrics__Enabled", "false");
            Set("RateLimiting__Policies__auth.login__PermitLimit", "2");
            if (disableLocalJwt)
            {
                Set("Auth__Schemes__LocalJwt__Enabled", "false");
                Set("Auth__Schemes__Oidc__Enabled", "true");
                Set("Auth__Schemes__Oidc__Authority", "https://identity.test");
                Set("Auth__Schemes__Oidc__Audience", "test-api");
            }
        }

        private void Set(string name, string value)
        {
            _previous.Add(name, Environment.GetEnvironmentVariable(name));
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach (var (name, value) in _previous)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
