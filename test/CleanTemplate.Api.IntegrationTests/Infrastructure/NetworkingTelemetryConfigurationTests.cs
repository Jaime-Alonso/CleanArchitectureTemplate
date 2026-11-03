using CleanTemplate.Crosscutting.Networking;
using CleanTemplate.Crosscutting.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

public sealed class NetworkingTelemetryConfigurationTests
{
    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies:0", "")]
    [InlineData("ForwardedHeaders:KnownProxies:0", "secret-invalid")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/33")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "::/129")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.1/24")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "secret-invalid")]
    public void InvalidForwardedEntriesFail(string key, string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddForwardedHeadersSupport(Config(key, value)));
        Assert.Contains(key[..key.LastIndexOf(':')], error.Message);
        Assert.DoesNotContain("secret-invalid", error.Message);
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies:0")]
    [InlineData("ForwardedHeaders:KnownNetworks:0")]
    [InlineData("Cors:AllowedOrigins:0")]
    public void NullListEntriesFail(string key)
    {
        var config = Config(new() { [key] = null });
        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            if (key.StartsWith("Cors:", StringComparison.Ordinal))
                new ServiceCollection().AddApiCors(config);
            else
                new ServiceCollection().AddForwardedHeadersSupport(config);
        });
        Assert.Contains(key[..key.LastIndexOf(':')], error.Message);
    }

    [Fact]
    public void ValidForwardedEntriesAreRetained()
    {
        var services = new ServiceCollection();
        services.AddForwardedHeadersSupport(Config(new()
        {
            ["ForwardedHeaders:KnownProxies:0"] = "192.0.2.1",
            ["ForwardedHeaders:KnownProxies:1"] = "2001:db8::1",
            ["ForwardedHeaders:KnownNetworks:0"] = "0.0.0.0/0",
            ["ForwardedHeaders:KnownNetworks:1"] = "2001:db8::/128"
        }));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Contains(options.KnownProxies, ip => ip.ToString() == "192.0.2.1");
        Assert.Contains(options.KnownProxies, ip => ip.ToString() == "2001:db8::1");
        Assert.Contains(options.KnownIPNetworks, network => network.ToString() == "0.0.0.0/0");
        Assert.Contains(options.KnownIPNetworks, network => network.ToString() == "2001:db8::/128");
    }

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("https://example.com/")]
    [InlineData("https://example.com/path")]
    [InlineData("https://example.com?x=1")]
    [InlineData("https://example.com#x")]
    [InlineData("https://user:secret-invalid@example.com")]
    [InlineData("not-a-url")]
    public void InvalidCorsOriginsFail(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddApiCors(Config("Cors:AllowedOrigins:0", value)));
        Assert.Contains("Cors:AllowedOrigins", error.Message);
        Assert.DoesNotContain("secret-invalid", error.Message);
    }

    [Fact]
    public void ValidCorsOriginsAreRetained()
    {
        var services = new ServiceCollection();
        services.AddApiCors(Config(new() { ["Cors:AllowedOrigins:0"] = "https://example.com", ["Cors:AllowedOrigins:1"] = "http://[::1]:8080" }));
        using var provider = services.BuildServiceProvider();
        var policy = provider.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy("ApiCors")!;
        Assert.Contains("https://example.com", policy.Origins);
        Assert.Contains("http://[::1]:8080", policy.Origins);
    }

    [Theory]
    [InlineData("OpenTelemetry:Otlp:Endpoint", "")]
    [InlineData("OpenTelemetry:Otlp:Endpoint", "/relative")]
    [InlineData("OpenTelemetry:Otlp:Endpoint", "ftp://example.com")]
    [InlineData("OpenTelemetry:Otlp:Endpoint", "https://user:secret-invalid@example.com")]
    [InlineData("OpenTelemetry:Otlp:Protocol", "")]
    [InlineData("OpenTelemetry:Otlp:Protocol", "secret-invalid")]
    [InlineData("OpenTelemetry:ServiceName", "")]
    [InlineData("OpenTelemetry:ServiceVersion", "")]
    [InlineData("OpenTelemetry:Traces:SamplingRatio", "NaN")]
    [InlineData("OpenTelemetry:Traces:SamplingRatio", "Infinity")]
    [InlineData("OpenTelemetry:Traces:SamplingRatio", "-0.1")]
    [InlineData("OpenTelemetry:Traces:SamplingRatio", "1.1")]
    [InlineData("OpenTelemetry:Traces:SamplingRatio", "secret-invalid")]
    public void InvalidTelemetryFailsWithoutValues(string key, string value)
    {
        var config = Config(new() { ["OpenTelemetry:Traces:Enabled"] = "true", [key] = value });
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddOpenTelemetryObservability(config, new TestEnvironment()));
        Assert.Contains(key, error.Message);
        Assert.DoesNotContain("secret-invalid", error.Message);
    }

    [Theory]
    [InlineData("grpc", "0")]
    [InlineData("http", "1")]
    [InlineData("httpprotobuf", "0.5")]
    public void ValidTelemetryIsAccepted(string protocol, string ratio)
    {
        var config = Config(new() { ["OpenTelemetry:Traces:Enabled"] = "true", ["OpenTelemetry:Otlp:Protocol"] = protocol, ["OpenTelemetry:Traces:SamplingRatio"] = ratio, ["OpenTelemetry:Otlp:Endpoint"] = "https://example.com:4317" });
        new ServiceCollection().AddOpenTelemetryObservability(config, new TestEnvironment());
    }

    [Fact]
    public void DisabledCapabilitiesDoNotValidateTheirSettings()
    {
        var config = Config(new() { ["OpenTelemetry:Metrics:Enabled"] = "true", ["OpenTelemetry:Traces:SamplingRatio"] = "not-a-number" });
        new ServiceCollection().AddOpenTelemetryObservability(config, new TestEnvironment());
        new ServiceCollection().AddOpenTelemetryObservability(Config("OpenTelemetry:Otlp:Endpoint", "invalid"), new TestEnvironment());
    }

    private static IConfiguration Config(string key, string value) => Config(new() { [key] = value });
    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
