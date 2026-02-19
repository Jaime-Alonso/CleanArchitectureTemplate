using System.Security.Claims;
using System.Threading;
using CleanTemplate.Api.Security.Options;
using CleanTemplate.Application.Abstractions;
using CleanTemplate.Application.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Api.Security;

public sealed class ProvisioningClaimsTransformation : IClaimsTransformation
{
    private readonly AuthOptions _authOptions;
    private readonly IExternalIdentityProvisioningService _provisioningService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ProvisioningClaimsTransformation> _logger;

    public ProvisioningClaimsTransformation(
        IOptions<AuthOptions> authOptions,
        IExternalIdentityProvisioningService provisioningService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ProvisioningClaimsTransformation> logger)
    {
        _authOptions = authOptions.Value;
        _provisioningService = provisioningService;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        if (principal.HasClaim(c => c.Type == InternalClaimTypes.InternalUserId))
        {
            return principal;
        }

        if (!_authOptions.Provisioning.Enabled || !_authOptions.Schemes.Oidc.Enabled)
        {
            return principal;
        }

        var issuer = principal.FindFirstValue("iss");
        if (string.IsNullOrWhiteSpace(issuer)
            || string.Equals(issuer, _authOptions.Schemes.LocalJwt.Issuer, StringComparison.OrdinalIgnoreCase))
        {
            return principal;
        }

        var claims = _authOptions.Schemes.Oidc.Claims;
        var providerSubject = principal.FindFirstValue(claims.Subject);
        if (string.IsNullOrWhiteSpace(providerSubject))
        {
            _logger.LogWarning("OIDC token did not include subject claim '{SubjectClaim}'", claims.Subject);
            return principal;
        }

        var externalIdentity = new ExternalIdentity
        {
            Provider = _authOptions.Schemes.Oidc.Provider,
            ProviderSubject = providerSubject,
            Email = principal.FindFirstValue(claims.Email),
            Name = principal.FindFirstValue(claims.Name),
            TenantId = principal.FindFirstValue(claims.TenantId),
            Issuer = issuer,
            Groups = GetMultiValueClaims(principal, claims.Groups),
            Roles = GetMultiValueClaims(principal, claims.Roles)
        };

        var cancellationToken = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var provisionResult = await _provisioningService.ProvisionAsync(externalIdentity, cancellationToken);

        var internalIdentity = new ClaimsIdentity();
        internalIdentity.AddClaim(new Claim(InternalClaimTypes.InternalUserId, provisionResult.InternalUserId.ToString()));
        internalIdentity.AddClaim(new Claim(ClaimTypes.NameIdentifier, provisionResult.InternalUserId.ToString()));
        internalIdentity.AddClaim(new Claim(InternalClaimTypes.AuthProvider, externalIdentity.Provider));

        foreach (var role in provisionResult.InternalRoles)
        {
            if (!principal.HasClaim(ClaimTypes.Role, role))
            {
                internalIdentity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }

        principal.AddIdentity(internalIdentity);

        _logger.LogInformation(
            "OIDC principal transformed. Provider {Provider}, internal user {InternalUserId}, provisioning occurred: {ProvisioningOccurred}",
            externalIdentity.Provider,
            provisionResult.InternalUserId,
            provisionResult.ProvisioningOccurred);

        return principal;
    }

    private static IReadOnlyCollection<string> GetMultiValueClaims(ClaimsPrincipal principal, string claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType))
        {
            return [];
        }

        return principal.Claims
            .Where(c => c.Type == claimType)
            .Select(c => c.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
