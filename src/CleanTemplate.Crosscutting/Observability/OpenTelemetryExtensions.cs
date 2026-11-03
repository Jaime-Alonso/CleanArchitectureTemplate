using CleanTemplate.Crosscutting.Observability.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CleanTemplate.Crosscutting.Observability;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddOpenTelemetryObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string serviceVersion)
    {
        IConfigurationSection section = configuration.GetSection(OpenTelemetryOptions.SectionName);
        bool tracesEnabled;
        bool metricsEnabled;
        try
        {
            tracesEnabled = section.GetValue<bool>("Traces:Enabled");
            metricsEnabled = section.GetValue<bool>("Metrics:Enabled");
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException("OpenTelemetry:Traces:Enabled and OpenTelemetry:Metrics:Enabled must be booleans.");
        }

        if (!tracesEnabled && !metricsEnabled)
        {
            return services;
        }

        OpenTelemetryOptions options;
        try
        {
            options = new OpenTelemetryOptions
            {
                ServiceName = section["ServiceName"] ?? "CleanTemplate.Host",
                Otlp = section.GetSection("Otlp").Get<OpenTelemetryOtlpOptions>() ?? new(),
                Traces = tracesEnabled ? section.GetSection("Traces").Get<OpenTelemetryTracesOptions>() ?? new() : new(),
                Metrics = new OpenTelemetryMetricsOptions { Enabled = metricsEnabled }
            };
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException("OpenTelemetry:Traces:SamplingRatio must be a valid number.");
        }

        if (string.IsNullOrWhiteSpace(options.ServiceName))
            throw new InvalidOperationException("OpenTelemetry:ServiceName must be configured.");
        if (string.IsNullOrWhiteSpace(serviceVersion))
            throw new ArgumentException("Artifact version is required.", nameof(serviceVersion));
        if (string.IsNullOrWhiteSpace(options.Otlp.Endpoint) || options.Otlp.Endpoint != options.Otlp.Endpoint.Trim() ||
            !Uri.TryCreate(options.Otlp.Endpoint, UriKind.Absolute, out Uri? endpointUri) ||
            (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(endpointUri.Host) || !string.IsNullOrEmpty(endpointUri.UserInfo) ||
            options.Otlp.Endpoint[(options.Otlp.Endpoint.IndexOf("://", StringComparison.Ordinal) + 3)..].Split('/', '?', '#')[0].Contains('@'))
            throw new InvalidOperationException("OpenTelemetry:Otlp:Endpoint must be an absolute HTTP(S) URL without credentials.");
        if (options.Traces.Enabled && (!double.IsFinite(options.Traces.SamplingRatio) || options.Traces.SamplingRatio < 0 || options.Traces.SamplingRatio > 1))
            throw new InvalidOperationException("OpenTelemetry:Traces:SamplingRatio must be finite and between 0 and 1.");

        string serviceName = options.ServiceName;
        string endpoint = options.Otlp.Endpoint;

        OtlpExportProtocol otlpProtocol = ResolveProtocol(options.Otlp.Protocol);
        double samplingRatio = options.Traces.SamplingRatio;

        ResourceBuilder resourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
            .AddAttributes(
            [
                new KeyValuePair<string, object>("deployment.environment", environment.EnvironmentName)
            ]);

        OpenTelemetryBuilder builder = services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
                .AddAttributes(
                [
                    new KeyValuePair<string, object>("deployment.environment", environment.EnvironmentName)
                ]));

        if (options.Traces.Enabled)
        {
            builder.WithTracing(tracing => ConfigureTracing(tracing, resourceBuilder, endpoint, otlpProtocol, samplingRatio));
        }

        if (options.Metrics.Enabled)
        {
            builder.WithMetrics(metrics => ConfigureMetrics(metrics, resourceBuilder, endpoint, otlpProtocol));
        }

        return services;
    }

    private static void ConfigureTracing(
        TracerProviderBuilder tracing,
        ResourceBuilder resourceBuilder,
        string endpoint,
        OtlpExportProtocol protocol,
        double samplingRatio)
    {
        tracing
            .SetResourceBuilder(resourceBuilder)
            .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)))
            .AddPrivateHttpTracing()
            .AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = new Uri(endpoint);
                exporter.Protocol = protocol;
            });
    }

    // Keep this configuration shared with local exporter tests and ahead of exporters.
    public static TracerProviderBuilder AddPrivateHttpTracing(this TracerProviderBuilder tracing) => tracing
        .AddAspNetCoreInstrumentation(options => options.RecordException = false)
        .AddHttpClientInstrumentation(options => options.RecordException = false)
        .AddProcessor(new HttpTracePrivacyProcessor());

    private static void ConfigureMetrics(
        MeterProviderBuilder metrics,
        ResourceBuilder resourceBuilder,
        string endpoint,
        OtlpExportProtocol protocol)
    {
        metrics
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("CleanTemplate.Http")
            .AddMeter("CleanTemplate.Security")
            .AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = new Uri(endpoint);
                exporter.Protocol = protocol;
            });
    }

    private static OtlpExportProtocol ResolveProtocol(string? protocol)
    {
        return protocol?.ToLowerInvariant() switch
        {
            "grpc" => OtlpExportProtocol.Grpc,
            "http" => OtlpExportProtocol.HttpProtobuf,
            "httpprotobuf" => OtlpExportProtocol.HttpProtobuf,
            _ => throw new InvalidOperationException("OpenTelemetry:Otlp:Protocol must be grpc, http or httpprotobuf.")
        };
    }
}
