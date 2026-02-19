namespace CleanTemplate.Application.Security;

public sealed record LocalTokenResult
{
    public required string AccessToken { get; init; }
    public required string JwtId { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
}
