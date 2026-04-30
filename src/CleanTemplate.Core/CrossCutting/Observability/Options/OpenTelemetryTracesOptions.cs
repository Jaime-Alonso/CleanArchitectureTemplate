namespace CleanTemplate.Core.CrossCutting.Observability.Options;

public sealed class OpenTelemetryTracesOptions
{
    public bool Enabled { get; init; }
    public double SamplingRatio { get; init; } = 1d;
}
