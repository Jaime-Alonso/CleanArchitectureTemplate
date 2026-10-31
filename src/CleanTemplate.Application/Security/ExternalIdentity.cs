namespace CleanTemplate.Application.Security;

public sealed record ExternalIdentity
{
    public required string Provider { get; init; }
    public required string ProviderSubject { get; init; }
    public required string Issuer { get; init; }
    public required DateTimeOffset TokenExpiresAtUtc { get; init; }
    public string? Email { get; init; }
    public bool EmailVerified { get; init; }
    public string? Name { get; init; }
    public string? TenantId { get; init; }
    public IReadOnlyCollection<string> Groups { get; init; } = [];
    public IReadOnlyCollection<string> Roles { get; init; } = [];
}
