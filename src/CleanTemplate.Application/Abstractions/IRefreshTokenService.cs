using CleanTemplate.Application.Security;

namespace CleanTemplate.Application.Abstractions;

public interface IRefreshTokenService
{
    Task<RefreshTokenIssueResult> IssueAsync(
        Guid userId,
        string jwtId,
        string? createdByIp,
        CancellationToken cancellationToken = default);

    Task<RefreshTokenConsumptionResult?> ConsumeAsync(
        string refreshToken,
        string? consumedByIp,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(
        string refreshToken,
        string? revokedByIp,
        CancellationToken cancellationToken = default);
}
