namespace CleanTemplate.Crosscutting.Observability.Options;

public sealed class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    public string ServiceName { get; init; } = "CleanTemplate.Host";
    public OpenTelemetryOtlpOptions Otlp { get; init; } = new();
    public OpenTelemetryTracesOptions Traces { get; init; } = new();
    public OpenTelemetryMetricsOptions Metrics { get; init; } = new();
}
