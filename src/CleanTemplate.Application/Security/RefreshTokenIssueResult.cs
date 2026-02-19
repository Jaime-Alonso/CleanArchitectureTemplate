namespace CleanTemplate.Application.Security;

public sealed record RefreshTokenIssueResult
{
    public required string RefreshToken { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
}
