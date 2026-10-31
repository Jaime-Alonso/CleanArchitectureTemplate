using System.Security.Claims;
using CleanTemplate.Api.Security.Options;
using CleanTemplate.Application.Contracts;
using CleanTemplate.Application.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Api.Security;

public sealed class ProvisioningClaimsTransformation(
    IOptions<AuthOptions> authOptions,
    IExternalIdentityProvisioningService provisioningService,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ProvisioningClaimsTransformation> logger) : IClaimsTransformation
{
    private readonly AuthOptions _authOptions = authOptions.Value;

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // ClaimsPrincipal may be transformed more than once. A JWT cannot materialize this CLR type.
        if (principal.Identities.Any(identity => identity is InternalClaimsIdentity))
        {
            return principal;
        }

        var source = principal.Identities.FirstOrDefault(identity => identity.IsAuthenticated
            && identity.AuthenticationType is "LocalJwt" or "Oidc");
        if (source is null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        // Drop all identities/roles/identifiers that could impersonate internal authorization.
        var reservedTypes = GetReservedTypes(source);
        var sanitized = new ClaimsPrincipal(new ClaimsIdentity(
            source.Claims.Where(claim => !reservedTypes.Contains(claim.Type)),
            source.AuthenticationType,
            source.NameClaimType,
            InternalClaimTypes.Role));

        if (source.AuthenticationType == "LocalJwt")
        {
            if (_authOptions.Schemes.LocalJwt.Enabled
                && Guid.TryParse(GetSingleValue(source, "sub"), out var userId)
                && userId != Guid.Empty)
            {
                sanitized.AddIdentity(new InternalClaimsIdentity(userId, "LocalJwt", GetValues(source, ClaimTypes.Role)));
            }

            return sanitized;
        }

        if (!_authOptions.Schemes.Oidc.Enabled || !_authOptions.Provisioning.Enabled)
        {
            return sanitized;
        }

        var claims = _authOptions.Schemes.Oidc.Claims;
        var issuer = GetSingleValue(source, "iss");
        var subject = GetSingleValue(source, "sub");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            logger.LogWarning("External provisioning rejected: a unique issuer and subject are required");
            return sanitized;
        }

        var externalIdentity = new ExternalIdentity
        {
            Provider = _authOptions.Schemes.Oidc.Provider,
            Issuer = issuer,
            ProviderSubject = subject,
            Email = GetSingleValue(source, claims.Email),
            EmailVerified = bool.TryParse(GetSingleValue(source, claims.EmailVerified), out var verified) && verified,
            Name = GetSingleValue(source, claims.Name),
            TenantId = GetSingleValue(source, claims.TenantId),
            Groups = GetValues(source, claims.Groups),
            Roles = GetValues(source, claims.Roles)
        };

        var cancellationToken = httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var result = await provisioningService.ProvisionAsync(externalIdentity, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure || result.Value.InternalUserId == Guid.Empty)
        {
            logger.LogWarning("External provisioning rejected. Code {Code}", result.FirstError?.Code ?? "Auth.InvalidInternalIdentity");
            return sanitized;
        }

        sanitized.AddIdentity(new InternalClaimsIdentity(
            result.Value.InternalUserId,
            externalIdentity.Provider,
            result.Value.InternalRoles));

        return sanitized;
    }

    private HashSet<string> GetReservedTypes(ClaimsIdentity source)
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            InternalClaimTypes.InternalUserId, InternalClaimTypes.AuthProvider, InternalClaimTypes.Role,
            ClaimTypes.NameIdentifier, ClaimTypes.Role, source.RoleClaimType,
            _authOptions.Schemes.Oidc.Claims.Roles, "role", "roles"
        };
    }

    private static string? GetSingleValue(ClaimsIdentity identity, string claimType)
    {
        var values = identity.Claims.Where(claim => claim.Type == claimType).Take(2).ToArray();
        return values.Length == 1 ? values[0].Value : null;
    }

    private static IReadOnlyCollection<string> GetValues(ClaimsIdentity identity, string claimType)
    {
        return identity.Claims.Where(claim => claim.Type == claimType)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
