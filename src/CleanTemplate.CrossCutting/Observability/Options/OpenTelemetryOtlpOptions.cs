namespace CleanTemplate.CrossCutting.Observability.Options;

public sealed class OpenTelemetryOtlpOptions
{
    public string Endpoint { get; init; } = "http://localhost:4317";
    public string Protocol { get; init; } = "grpc";
}
