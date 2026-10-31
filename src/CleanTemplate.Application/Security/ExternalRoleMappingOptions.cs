namespace CleanTemplate.Application.Security;

public sealed class ExternalRoleMappingOptions
{
    public bool Enabled { get; init; } = true;
    public int RoleGrantLifetimeMinutes { get; init; } = 5;
    public string DefaultRole { get; init; } = "User";
    public ExternalRoleMappingRuleOptions Admin { get; init; } = new();
    public ExternalRoleMappingRuleOptions User { get; init; } = new();
}
