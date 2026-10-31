using CleanTemplate.Application.Security;

namespace CleanTemplate.Application.Contracts;

public interface IUserAuthorizationService
{
    /// <summary>Reads current permissions without trusting token roles. Returns null for missing or locked users.</summary>
    Task<UserAuthorization?> GetAsync(Guid userId, CancellationToken cancellationToken = default);
}
