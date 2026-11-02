using CleanTemplate.Application.Security;

namespace CleanTemplate.Application.Contracts.Identity;

public interface IExternalRoleMapper
{
    IReadOnlyCollection<string> MapRoles(ExternalIdentity externalIdentity);
}
