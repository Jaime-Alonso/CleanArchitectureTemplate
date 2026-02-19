namespace CleanTemplate.Infrastructure.Security;

public sealed class RefreshTokenCleanupOptions
{
    public const string SectionName = "Auth:RefreshTokenCleanup";

    public bool Enabled { get; init; } = true;
    public int IntervalMinutes { get; init; } = 60;
    public int RevokedRetentionDays { get; init; } = 7;
}
