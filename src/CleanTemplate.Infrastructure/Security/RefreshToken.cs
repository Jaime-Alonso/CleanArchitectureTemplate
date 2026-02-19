using System;

namespace CleanTemplate.Infrastructure.Security;

public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    private RefreshToken(
        Guid userId,
        string jwtId,
        string tokenHash,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        string? createdByIp)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));

        if (string.IsNullOrWhiteSpace(jwtId))
            throw new ArgumentException("JwtId cannot be empty.", nameof(jwtId));

        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("TokenHash cannot be empty.", nameof(tokenHash));

        if (expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("ExpiresAtUtc must be greater than CreatedAtUtc.", nameof(expiresAtUtc));

        Id = Guid.NewGuid();
        UserId = userId;
        JwtId = jwtId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        CreatedByIp = createdByIp;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public string JwtId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public string? CreatedByIp { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? RevokedByIp { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    public static RefreshToken Create(
        Guid userId,
        string jwtId,
        string tokenHash,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        string? createdByIp)
    {
        return new RefreshToken(userId, jwtId, tokenHash, createdAtUtc, expiresAtUtc, createdByIp);
    }

    public bool CanBeConsumed(DateTime nowUtc)
    {
        return RevokedAtUtc is null && ExpiresAtUtc > nowUtc;
    }

    public bool Revoke(DateTime revokedAtUtc, string? revokedByIp, string? replacedByTokenHash = null)
    {
        if (RevokedAtUtc is not null)
            return false;

        RevokedAtUtc = revokedAtUtc;
        RevokedByIp = revokedByIp;
        ReplacedByTokenHash = replacedByTokenHash;
        return true;
    }
}
