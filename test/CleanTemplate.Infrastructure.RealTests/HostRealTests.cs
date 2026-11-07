using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CleanTemplate.Application.Contracts.Identity;
using CleanTemplate.Application.Contracts.Persistence;
using CleanTemplate.Application.Security;
using CleanTemplate.Domain.Entities;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CleanTemplate.Infrastructure.RealTests;

[Trait("Category", "InfrastructureReal")]
public sealed class HostRealTests
{
    private static readonly SemaphoreSlim HostStartup = new(1, 1);
    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    public async Task IndependentTestingHostsReadOnlyTheirOwnDatabase(string provider)
    {
        var id = Guid.NewGuid();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        await Task.WhenAll(Exercise("first"), Exercise("second"));

        async Task Exercise(string name)
        {
            try { await ExerciseCore(name); }
            catch { ready.TrySetCanceled(); throw; }
        }

        async Task ExerciseCore(string name)
        {
            await using var database = await RealDatabase.StartAsync(provider);
            await using var host = new RealHostFactory(database);
            await HostStartup.WaitAsync();
            HttpClient client;
            try { client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") }); }
            finally { HostStartup.Release(); }
            using var ownedClient = client;
            var environment = host.Services.GetRequiredService<IHostEnvironment>();
            Assert.True(environment.IsEnvironment("Testing"));
            Assert.False(environment.IsDevelopment());
            var configuration = (IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>();
            Assert.All(configuration.Providers, p => Assert.IsType<MemoryConfigurationProvider>(p));
            Assert.Equal(provider, configuration["DatabaseProvider"]);
            Assert.True(configuration.GetConnectionString("DefaultConnection") == database.CreateConfiguration()["ConnectionStrings:DefaultConnection"]);
            Assert.Equal("false", configuration["Auth:RefreshTokenCleanup:Enabled"]);
            Assert.Equal("false", configuration["OpenTelemetry:Traces:Enabled"]);
            Assert.Equal("false", configuration["OpenTelemetry:Metrics:Enabled"]);
            var health = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            Assert.True(health.Status == HealthStatus.Healthy, string.Join(", ", health.Entries.Select(x => x.Value.Exception?.GetType().Name ?? x.Value.Status.ToString())));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(provider == "SqlServer" ? "Microsoft.EntityFrameworkCore.SqlServer" : "Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            var expectedConnection = database.CreateConfiguration()["ConnectionStrings:DefaultConnection"];
            var actualConnection = db.Database.GetConnectionString();
            if (provider == "SqlServer")
            {
                var expected = new SqlConnectionStringBuilder(expectedConnection);
                var actual = new SqlConnectionStringBuilder(actualConnection);
                Assert.True(expected.DataSource == actual.DataSource && expected.InitialCatalog == actual.InitialCatalog);
            }
            else
            {
                var expected = new NpgsqlConnectionStringBuilder(expectedConnection);
                var actual = new NpgsqlConnectionStringBuilder(actualConnection);
                Assert.True(expected.Host == actual.Host && expected.Port == actual.Port && expected.Database == actual.Database);
            }
            Assert.False(await db.Users.AnyAsync());
            Assert.False(await db.Roles.AnyAsync());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>("User"))).Succeeded);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = name, Email = name + "@example.test" };
            Assert.True((await users.CreateAsync(user)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "User")).Succeeded);
            var token = scope.ServiceProvider.GetRequiredService<ILocalJwtTokenGenerator>()
                .Generate(new AuthUser { Id = user.Id, Email = user.Email }, ["User"]);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            var product = new Product(name, null, 1m, 1);
            db.Entry(product).Property(x => x.Id).CurrentValue = id;
            db.Add(product);
            Assert.True(await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
            if (Interlocked.Increment(ref started) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromMinutes(2));
            using var response = await client.GetAsync($"/api/products/{id}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.Equal(name, body.GetProperty("name").GetString());
        }
    }
}
