using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string AdminPassword = "test-only-SeedPassword_123!";
    private readonly string _environment;
    private readonly Dictionary<string, string?> _settings;

    public TestWebApplicationFactory() : this("Testing") { }

    internal TestWebApplicationFactory(string environment, IReadOnlyDictionary<string, string?>? settings = null)
    {
        _environment = environment;
        _settings = new Dictionary<string, string?>
        {
            ["DatabaseProvider"] = "SqliteInMemory",
            ["ConnectionStrings:DefaultConnection"] = $"Data Source=http-{Guid.NewGuid():N};Mode=Memory;Cache=Shared",
            ["ProductReads:Mode"] = "Primary",
            ["Caching:Provider"] = "Memory",
            ["Auth:Schemes:LocalJwt:Enabled"] = "true",
            ["Auth:Schemes:LocalJwt:Issuer"] = "http-tests",
            ["Auth:Schemes:LocalJwt:Audience"] = "http-tests-api",
            ["Auth:Schemes:LocalJwt:SigningKey"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ["Auth:Schemes:LocalJwt:ActiveSigningKeyId"] = "http-tests",
            ["Auth:Schemes:Oidc:Enabled"] = "false",
            ["Auth:Provisioning:Enabled"] = "false",
            ["Auth:RefreshTokenCleanup:Enabled"] = "false",
            ["IdentitySeed:AdminEmail"] = "admin@local.template",
            ["IdentitySeed:AdminPassword"] = AdminPassword,
            ["RateLimiting:Global:PermitLimit"] = "120",
            ["RateLimiting:Global:WindowSeconds"] = "60",
            ["RateLimiting:Policies:auth.login:PermitLimit"] = "5",
            ["RateLimiting:Policies:auth.login:WindowSeconds"] = "60",
            ["RateLimiting:Policies:auth.refresh:PermitLimit"] = "20",
            ["RateLimiting:Policies:auth.refresh:WindowSeconds"] = "60",
            ["RateLimiting:Policies:auth.logout:PermitLimit"] = "30",
            ["RateLimiting:Policies:auth.logout:WindowSeconds"] = "60",
            ["OpenTelemetry:Traces:Enabled"] = "false",
            ["OpenTelemetry:Metrics:Enabled"] = "false",
            ["Serilog:MinimumLevel:Default"] = "Information",
            ["Serilog:MinimumLevel:Override:Microsoft"] = "Warning",
            ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore.Hosting"] = "Warning",
            ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore.Mvc"] = "Warning",
            ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware"] = "Fatal",
            ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore.HttpLogging"] = "Fatal",
            ["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"] = "Fatal",
            ["Serilog:MinimumLevel:Override:System.Net.Http.HttpClient"] = "Warning",
            ["Serilog:MinimumLevel:Override:System"] = "Warning"
        };
        if (settings is not null)
            foreach (var (key, value) in settings)
                _settings[key] = value;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // WAF's normal app-configuration callback is too late for Program's startup snapshot.
        // Use the public hosting-startup extension point to replace sources inside CreateBuilder.
        var previous = TestConfigurationStartup.Settings.Value;
        TestConfigurationStartup.Settings.Value = _settings;
        try
        {
            builder.ConfigureHostConfiguration(configuration => configuration
                .AddInMemoryCollection(_settings)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [WebHostDefaults.HostingStartupAssembliesKey] = typeof(TestConfigurationStartup).Assembly.GetName().Name,
                    [WebHostDefaults.PreventHostingStartupKey] = "false",
                    [WebHostDefaults.HostingStartupExcludeAssembliesKey] = string.Empty
                }));
            return base.CreateHost(builder);
        }
        finally { TestConfigurationStartup.Settings.Value = previous; }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(_settings);
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.BaseAddress = new Uri("https://localhost");
    }
}
