using System.Security.Claims;
using CleanTemplate.Application.Security;

namespace CleanTemplate.Api.Security;

// This runtime type, not a claim supplied by a token, marks identities created by the application.
internal sealed class InternalClaimsIdentity : ClaimsIdentity
{
    internal InternalClaimsIdentity(Guid userId, string provider, IEnumerable<string> roles)
        : base("Internal", ClaimTypes.NameIdentifier, InternalClaimTypes.Role)
    {
        AddClaim(new Claim(InternalClaimTypes.InternalUserId, userId.ToString()));
        AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
        AddClaim(new Claim(InternalClaimTypes.AuthProvider, provider));

        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            AddClaim(new Claim(InternalClaimTypes.Role, role));
        }
    }
}
