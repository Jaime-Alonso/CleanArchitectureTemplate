using CleanTemplate.Application.Security;

namespace CleanTemplate.Application.Abstractions;

public interface ILocalJwtTokenGenerator
{
    LocalTokenResult Generate(AuthUser user, IReadOnlyCollection<string> roles);
}
