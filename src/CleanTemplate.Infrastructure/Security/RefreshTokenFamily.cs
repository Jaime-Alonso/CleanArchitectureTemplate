namespace CleanTemplate.Infrastructure.Security;

public sealed class RefreshTokenFamily
{
    private RefreshTokenFamily() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? RevocationReason { get; private set; }
    public string? SecurityStamp { get; private set; }
    public long Version { get; private set; }

    public static RefreshTokenFamily Create(Guid userId, DateTime now, DateTime expiresAtUtc, string? securityStamp) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        CreatedAtUtc = now,
        ExpiresAtUtc = expiresAtUtc,
        SecurityStamp = securityStamp
    };
}
