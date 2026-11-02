using CleanTemplate.Application.Security;

namespace CleanTemplate.Application.Contracts;

public interface IRefreshTokenService
{
    Task<RefreshTokenIssueResult> IssueAsync(
        Guid userId,
        string jwtId,
        string? createdByIp,
        CancellationToken cancellationToken = default);

    // Advisory lookup only: it neither consumes nor reserves the token.
    // A successful lookup does not guarantee that RotateAsync will succeed.
    Task<RefreshTokenConsumptionResult?> FindValidAsync(
        string refreshToken,
        string? consumedByIp,
        CancellationToken cancellationToken = default);

    // Atomically consumes a valid token belonging to userId and persists its replacement.
    // Returns null if another request already consumed/revoked it, or it expired.
    Task<RefreshTokenIssueResult?> RotateAsync(
        string refreshToken,
        Guid userId,
        string jwtId,
        string? createdByIp,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(
        string refreshToken,
        string? revokedByIp,
        CancellationToken cancellationToken = default);
}
