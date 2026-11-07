using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CleanTemplate.Infrastructure.RealTests;

internal sealed class RealHostFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings;

    public RealHostFactory(RealDatabase database)
    {
        _settings = database.CreateConfiguration();
        _settings["Auth:Schemes:LocalJwt:Enabled"] = "true";
        _settings["Auth:Schemes:LocalJwt:Issuer"] = "real-tests";
        _settings["Auth:Schemes:LocalJwt:Audience"] = "real-tests-api";
        _settings["Auth:Schemes:LocalJwt:SigningKey"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _settings["Auth:Schemes:LocalJwt:ActiveSigningKeyId"] = "real-tests";
        _settings["Auth:Schemes:Oidc:Enabled"] = "false";
        _settings["Auth:Provisioning:Enabled"] = "false";
        _settings["Auth:RefreshTokenCleanup:Enabled"] = "false";
        _settings["Caching:Provider"] = "Memory";
        _settings["OpenTelemetry:Traces:Enabled"] = "false";
        _settings["OpenTelemetry:Metrics:Enabled"] = "false";
        _settings["HealthChecks:TimeoutSeconds"] = "30";
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(_settings));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(_settings);
        });
    }
}
