namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

// Program uses a process-wide Serilog bootstrap logger. Full hosts must not initialize it concurrently.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostIntegrationCollection
{
    public const string Name = "Host integration";
}
