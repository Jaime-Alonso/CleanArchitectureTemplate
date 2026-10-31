namespace CleanTemplate.Infrastructure.Security;

/// <summary>An expiring role owned by one external login, independent of manual Identity assignments.</summary>
public sealed class ExternalRoleGrant
{
    private ExternalRoleGrant() { }

    public ExternalRoleGrant(Guid userId, string loginProvider, string providerKey, Guid roleId, DateTime validUntilUtc)
    {
        if (userId == Guid.Empty || roleId == Guid.Empty)
            throw new ArgumentException("A role grant requires user and role identifiers.");

        ArgumentException.ThrowIfNullOrWhiteSpace(loginProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);

        UserId = userId;
        LoginProvider = loginProvider;
        ProviderKey = providerKey;
        RoleId = roleId;
        SetExpiration(validUntilUtc);
    }

    public Guid UserId { get; private set; }
    public string LoginProvider { get; private set; } = null!;
    public string ProviderKey { get; private set; } = null!;
    public Guid RoleId { get; private set; }
    public DateTime ValidUntilUtc { get; private set; }

    public void SetExpiration(DateTime validUntilUtc)
    {
        if (validUntilUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Role grant expiration must be UTC.", nameof(validUntilUtc));

        ValidUntilUtc = validUntilUtc;
    }
}
