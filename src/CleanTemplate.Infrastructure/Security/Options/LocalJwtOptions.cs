namespace CleanTemplate.Infrastructure.Security.Options;

public sealed class LocalJwtOptions
{
    public bool Enabled { get; init; } = true;
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string SigningKey { get; init; } = string.Empty;
    public string ActiveSigningKeyId { get; init; } = "v1";
    public IReadOnlyCollection<JwtSigningKeyOptions> SigningKeys { get; init; } = [];
    public int AccessTokenMinutes { get; init; } = 30;
    public int RefreshTokenDays { get; init; } = 14;
}
