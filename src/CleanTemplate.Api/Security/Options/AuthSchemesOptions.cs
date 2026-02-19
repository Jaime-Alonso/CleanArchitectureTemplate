namespace CleanTemplate.Api.Security.Options;

public sealed class AuthSchemesOptions
{
    public LocalJwtOptions LocalJwt { get; init; } = new();
    public OidcOptions Oidc { get; init; } = new();
}
