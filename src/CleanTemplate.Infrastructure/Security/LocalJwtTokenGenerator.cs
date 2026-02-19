using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CleanTemplate.Application.Abstractions;
using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Security.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CleanTemplate.Infrastructure.Security;

public sealed class LocalJwtTokenGenerator : ILocalJwtTokenGenerator
{
    private readonly LocalJwtOptions _localJwtOptions;
    private readonly TimeProvider _timeProvider;

    public LocalJwtTokenGenerator(IOptions<LocalJwtOptions> localJwtOptions, TimeProvider timeProvider)
    {
        _localJwtOptions = localJwtOptions.Value;
        _timeProvider = timeProvider;
    }

    public LocalTokenResult Generate(AuthUser user, IReadOnlyCollection<string> roles)
    {
        var localJwtOptions = _localJwtOptions;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var jwtId = Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, jwtId),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(InternalClaimTypes.InternalUserId, user.Id.ToString()),
            new(InternalClaimTypes.AuthProvider, "LocalJwt")
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var signingKey = LocalJwtSigningKeyResolver.GetActiveSigningKey(localJwtOptions);
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var expiresAtUtc = now.AddMinutes(localJwtOptions.AccessTokenMinutes);

        var token = new JwtSecurityToken(
            issuer: localJwtOptions.Issuer,
            audience: localJwtOptions.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new LocalTokenResult
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            JwtId = jwtId,
            ExpiresAtUtc = expiresAtUtc
        };
    }
}
