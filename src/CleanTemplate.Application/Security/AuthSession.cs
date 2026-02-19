namespace CleanTemplate.Application.Security;

public sealed record AuthSession
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTime AccessTokenExpiresAtUtc { get; init; }
    public required DateTime RefreshTokenExpiresAtUtc { get; init; }
}
