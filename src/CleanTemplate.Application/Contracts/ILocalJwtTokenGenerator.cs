using CleanTemplate.Application.Security;

namespace CleanTemplate.Application.Contracts;

public interface ILocalJwtTokenGenerator
{
    LocalTokenResult Generate(AuthUser user, IReadOnlyCollection<string> roles);
}
