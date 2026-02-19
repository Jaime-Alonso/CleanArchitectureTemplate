using CleanTemplate.Application.Abstractions;
using CleanTemplate.Application.Security;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Security;

public sealed class ExternalRoleMapper : IExternalRoleMapper
{
    private readonly ExternalRoleMappingOptions _roleMappingOptions;

    public ExternalRoleMapper(IOptions<ExternalRoleMappingOptions> roleMappingOptions)
    {
        _roleMappingOptions = roleMappingOptions.Value;
    }

    public IReadOnlyCollection<string> MapRoles(ExternalIdentity externalIdentity)
    {
        var normalizedEmail = externalIdentity.Email?.Trim().ToLowerInvariant();
        var domain = normalizedEmail?.Split('@').LastOrDefault();

        if (MatchesExactEmail(_roleMappingOptions.Admin.Emails, normalizedEmail)
            || MatchesDomain(_roleMappingOptions.Admin.Domains, domain)
            || MatchesAny(_roleMappingOptions.Admin.Groups, externalIdentity.Groups)
            || MatchesAny(_roleMappingOptions.Admin.Roles, externalIdentity.Roles))
        {
            return ["Admin"];
        }

        if (MatchesAny(_roleMappingOptions.User.Groups, externalIdentity.Groups)
            || MatchesAny(_roleMappingOptions.User.Roles, externalIdentity.Roles))
        {
            return ["User"];
        }

        return [_roleMappingOptions.DefaultRole];
    }

    private static bool MatchesExactEmail(IEnumerable<string> configuredEmails, string? normalizedEmail)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return false;
        }

        return configuredEmails.Any(email => string.Equals(email.Trim(), normalizedEmail, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesDomain(IEnumerable<string> configuredDomains, string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }

        return configuredDomains.Any(configuredDomain => string.Equals(configuredDomain.Trim(), domain, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesAny(IEnumerable<string> configuredValues, IEnumerable<string> incomingValues)
    {
        var normalizedConfiguredValues = new HashSet<string>(configuredValues.Select(v => v.Trim()), StringComparer.OrdinalIgnoreCase);
        if (normalizedConfiguredValues.Count == 0)
        {
            return false;
        }

        foreach (var incomingValue in incomingValues)
        {
            if (normalizedConfiguredValues.Contains(incomingValue.Trim()))
            {
                return true;
            }
        }

        return false;
    }
}
