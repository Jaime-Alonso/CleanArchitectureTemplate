namespace CleanTemplate.Application.Security;

public sealed class ExternalRoleMappingRuleOptions
{
    public IReadOnlyCollection<string> Emails { get; init; } = [];
    public IReadOnlyCollection<string> Domains { get; init; } = [];
    public IReadOnlyCollection<string> Groups { get; init; } = [];
    public IReadOnlyCollection<string> Roles { get; init; } = [];
}
