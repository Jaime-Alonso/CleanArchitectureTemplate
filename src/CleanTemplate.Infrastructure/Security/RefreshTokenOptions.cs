namespace CleanTemplate.Infrastructure.Security;

public sealed class RefreshTokenOptions
{
    public int RefreshTokenDays { get; init; } = 14;
    public int AbsoluteSessionDays { get; init; } = 30;
}
