using System;
using System.Collections.Generic;
using CleanTemplate.Core.CrossCutting.Observability.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CleanTemplate.Core.CrossCutting.Observability;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddOpenTelemetryObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = configuration
            .GetSection(OpenTelemetryOptions.SectionName)
            .Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();

        if (!options.Traces.Enabled && !options.Metrics.Enabled)
        {
            return services;
        }

        var serviceName = string.IsNullOrWhiteSpace(options.ServiceName)
            ? environment.ApplicationName
            : options.ServiceName;

        var serviceVersion = string.IsNullOrWhiteSpace(options.ServiceVersion)
            ? "1.0.0"
            : options.ServiceVersion;

        var endpoint = string.IsNullOrWhiteSpace(options.Otlp.Endpoint)
            ? "http://localhost:4317"
            : options.Otlp.Endpoint;

        var otlpProtocol = ResolveProtocol(options.Otlp.Protocol);
        var samplingRatio = Math.Clamp(options.Traces.SamplingRatio, 0d, 1d);

        var resourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
            .AddAttributes(
            [
                new KeyValuePair<string, object>("deployment.environment", environment.EnvironmentName)
            ]);

        var builder = services
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
