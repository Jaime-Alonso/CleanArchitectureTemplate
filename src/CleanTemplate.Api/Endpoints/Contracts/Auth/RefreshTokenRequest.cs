namespace CleanTemplate.Api.Endpoints.Contracts.Auth;

public sealed record RefreshTokenRequest
{
    public string RefreshToken { get; init; } = string.Empty;
}
