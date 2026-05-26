using CleanTemplate.Application.Security;

namespace CleanTemplate.Application.Contracts;

public interface IExternalRoleMapper
{
    IReadOnlyCollection<string> MapRoles(ExternalIdentity externalIdentity);
}
