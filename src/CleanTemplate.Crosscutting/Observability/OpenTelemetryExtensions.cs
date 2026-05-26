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
        IHostEnvironment environment)
    {
        OpenTelemetryOptions options = configuration
            .GetSection(OpenTelemetryOptions.SectionName)
            .Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();

        if (!options.Traces.Enabled && !options.Metrics.Enabled)
        {
            return services;
        }

        string serviceName = string.IsNullOrWhiteSpace(options.ServiceName)
            ? environment.ApplicationName
            : options.ServiceName;

        string serviceVersion = string.IsNullOrWhiteSpace(options.ServiceVersion)
            ? "1.0.0"
            : options.ServiceVersion;

        string endpoint = string.IsNullOrWhiteSpace(options.Otlp.Endpoint)
            ? "http://localhost:4317"
            : options.Otlp.Endpoint;

        OtlpExportProtocol otlpProtocol = ResolveProtocol(options.Otlp.Protocol);
        double samplingRatio = Math.Clamp(options.Traces.SamplingRatio, 0d, 1d);

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
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = new Uri(endpoint);
                exporter.Protocol = protocol;
            });
    }

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
            .AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = new Uri(endpoint);
                exporter.Protocol = protocol;
            });
    }

    private static OtlpExportProtocol ResolveProtocol(string? protocol)
    {
        return protocol?.Trim().ToLowerInvariant() switch
        {
            "http" => OtlpExportProtocol.HttpProtobuf,
            "httpprotobuf" => OtlpExportProtocol.HttpProtobuf,
            _ => OtlpExportProtocol.Grpc
        };
    }
}
