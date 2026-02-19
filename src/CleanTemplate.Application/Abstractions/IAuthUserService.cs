using CleanTemplate.Application.Security;

namespace CleanTemplate.Application.Abstractions;

public interface IAuthUserService
{
    Task<AuthUser?> FindByEmailOrUserNameAsync(string emailOrUserName, CancellationToken cancellationToken = default);

    Task<AuthUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default);

    Task AccessFailedAsync(Guid userId, CancellationToken cancellationToken = default);

    Task ResetAccessFailedCountAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default);
}
