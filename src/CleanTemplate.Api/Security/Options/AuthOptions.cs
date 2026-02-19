using CleanTemplate.Application.Security;

namespace CleanTemplate.Api.Security.Options;

public sealed class AuthOptions
{
    public AuthSchemesOptions Schemes { get; init; } = new();
    public ExternalRoleMappingOptions Provisioning { get; init; } = new();
}
