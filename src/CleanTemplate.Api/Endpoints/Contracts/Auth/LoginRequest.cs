namespace CleanTemplate.Api.Endpoints.Contracts.Auth;

public sealed record LoginRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
