using CleanTemplate.Application.Security;

namespace CleanTemplate.Application.Abstractions;

public interface IExternalRoleMapper
{
    IReadOnlyCollection<string> MapRoles(ExternalIdentity externalIdentity);
}
