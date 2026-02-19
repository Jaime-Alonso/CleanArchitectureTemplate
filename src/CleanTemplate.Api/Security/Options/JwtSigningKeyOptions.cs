namespace CleanTemplate.Api.Security.Options;

public sealed class JwtSigningKeyOptions
{
    public string KeyId { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
