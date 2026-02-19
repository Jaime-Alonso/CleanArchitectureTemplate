namespace CleanTemplate.Application.Security;

public sealed record ExternalIdentity
{
    public required string Provider { get; init; }
    public required string ProviderSubject { get; init; }
    public string? Email { get; init; }
    public string? Name { get; init; }
    public string? TenantId { get; init; }
    public string? Issuer { get; init; }
    public IReadOnlyCollection<string> Groups { get; init; } = [];
    public IReadOnlyCollection<string> Roles { get; init; } = [];
}
