namespace CleanTemplate.Api.Endpoints.Contracts.Auth;

public sealed record LoginResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public string TokenType { get; init; } = string.Empty;
    public int ExpiresIn { get; init; }
    public DateTime RefreshTokenExpiresAtUtc { get; init; }
}
