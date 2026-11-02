using CleanTemplate.Application.Security;

namespace CleanTemplate.Application.Contracts.Identity;

public interface IRefreshTokenService
{
    Task<RefreshTokenIssueResult> IssueAsync(
        Guid userId,
        string jwtId,
        string? createdByIp,
        CancellationToken cancellationToken = default);

    // Does not consume/reserve a valid token; presenting a consumed ancestor revokes its family.
    // A successful lookup does not guarantee that RotateAsync will succeed.
    Task<RefreshTokenConsumptionResult?> FindValidAsync(
        string refreshToken,
        string? consumedByIp,
        CancellationToken cancellationToken = default);

    // Atomically consumes a valid token belonging to userId and persists its replacement.
    // Returns null if another request already consumed/revoked it, or it expired.
    // Concurrent reuse revokes the entire family, including a concurrent winner's replacement.
    Task<RefreshTokenIssueResult?> RotateAsync(
        string refreshToken,
        Guid userId,
        string jwtId,
        string? createdByIp,
        CancellationToken cancellationToken = default);

    // Revokes the presented token's family (also when presenting a consumed ancestor).
    Task<bool> RevokeAsync(
        string refreshToken,
        string? revokedByIp,
        CancellationToken cancellationToken = default);

    Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
