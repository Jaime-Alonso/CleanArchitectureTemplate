namespace CleanTemplate.Api.Security.Options;

public sealed class OidcClaimsOptions
{
    public string Email { get; init; } = "email";
    public string Name { get; init; } = "name";
    public string Subject { get; init; } = "sub";
    public string Groups { get; init; } = "groups";
    public string Roles { get; init; } = "roles";
    public string TenantId { get; init; } = "tid";
}
