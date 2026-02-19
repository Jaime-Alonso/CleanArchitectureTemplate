namespace CleanTemplate.Infrastructure.Security;

public sealed class RefreshTokenOptions
{
    public int RefreshTokenDays { get; init; } = 14;
}
