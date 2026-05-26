namespace CleanTemplate.Crosscutting.Networking.Options;

public sealed class ForwardedHeadersTrustOptions
{
    public const string SectionName = "ForwardedHeaders";

    public IReadOnlyList<string> KnownProxies { get; init; } = [];
    public IReadOnlyList<string> KnownNetworks { get; init; } = [];
}
