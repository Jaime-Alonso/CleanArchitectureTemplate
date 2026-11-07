namespace CleanTemplate.Api.Endpoints.Contracts.Auth;

public sealed record LoginResponse
{
    public required string AccessToken { get; init; } = string.Empty;
    public required string RefreshToken { get; init; } = string.Empty;
    public required string TokenType { get; init; } = string.Empty;
    public required int ExpiresIn { get; init; }
    public required DateTime RefreshTokenExpiresAtUtc { get; init; }
}
