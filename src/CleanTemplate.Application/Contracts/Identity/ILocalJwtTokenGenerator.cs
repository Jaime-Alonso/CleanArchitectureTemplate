using CleanTemplate.Application.Security;

namespace CleanTemplate.Application.Contracts.Identity;

public interface ILocalJwtTokenGenerator
{
    LocalTokenResult Generate(AuthUser user, IReadOnlyCollection<string> roles);
}
