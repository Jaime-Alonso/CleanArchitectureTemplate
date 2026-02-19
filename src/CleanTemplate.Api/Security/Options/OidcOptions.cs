namespace CleanTemplate.Api.Security.Options;

public sealed class OidcOptions
{
    public bool Enabled { get; init; }
    public string Authority { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string Provider { get; init; } = "Oidc";
    public OidcClaimsOptions Claims { get; init; } = new();
}
