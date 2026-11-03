using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Api.Security.Options;

public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        if (options.Provisioning.RoleGrantLifetimeMinutes is < 1 or > 60)
        {
            return ValidateOptionsResult.Fail("Auth:Provisioning:RoleGrantLifetimeMinutes must be between 1 and 60.");
        }

        if (!options.Schemes.LocalJwt.Enabled && !options.Schemes.Oidc.Enabled)
        {
            return ValidateOptionsResult.Fail("At least one authentication scheme (LocalJwt or Oidc) must be enabled.");
        }

        if (options.Schemes.LocalJwt.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Schemes.LocalJwt.Issuer))
            {
                return ValidateOptionsResult.Fail("Auth:Schemes:LocalJwt:Issuer is required when LocalJwt is enabled.");
            }

            if (string.IsNullOrWhiteSpace(options.Schemes.LocalJwt.Audience))
            {
                return ValidateOptionsResult.Fail("Auth:Schemes:LocalJwt:Audience is required when LocalJwt is enabled.");
            }

            var signingKeys = options.Schemes.LocalJwt.SigningKeys;
            if (string.IsNullOrWhiteSpace(options.Schemes.LocalJwt.ActiveSigningKeyId))
                return ValidateOptionsResult.Fail("Auth:Schemes:LocalJwt:ActiveSigningKeyId is required.");
            if (signingKeys.Count > 0)
            {
                if (signingKeys.Select(x => x.KeyId).Distinct(StringComparer.Ordinal).Count() != signingKeys.Count)
                    return ValidateOptionsResult.Fail("Auth:Schemes:LocalJwt:SigningKeys contains duplicate KeyId entries.");
                if (string.IsNullOrWhiteSpace(options.Schemes.LocalJwt.ActiveSigningKeyId))
                {
                    return ValidateOptionsResult.Fail("Auth:Schemes:LocalJwt:ActiveSigningKeyId is required when SigningKeys are configured.");
                }

                if (!signingKeys.Any(x => string.Equals(x.KeyId, options.Schemes.LocalJwt.ActiveSigningKeyId, StringComparison.Ordinal)))
                {
                    return ValidateOptionsResult.Fail("Auth:Schemes:LocalJwt:ActiveSigningKeyId must exist in SigningKeys.");
                }

                if (signingKeys.Any(x => string.IsNullOrWhiteSpace(x.KeyId) || string.IsNullOrWhiteSpace(x.Value) || x.Value.Length < 32))
                {
                    return ValidateOptionsResult.Fail("All Auth:Schemes:LocalJwt:SigningKeys entries require KeyId and a Value with at least 32 characters.");
                }
            }
            else if (string.IsNullOrWhiteSpace(options.Schemes.LocalJwt.SigningKey) || options.Schemes.LocalJwt.SigningKey.Length < 32)
            {
                return ValidateOptionsResult.Fail("Auth:Schemes:LocalJwt:SigningKey must be at least 32 characters when LocalJwt is enabled.");
            }

            if (options.Schemes.LocalJwt.RefreshTokenDays < 1 || options.Schemes.LocalJwt.RefreshTokenDays > 90)
            {
                return ValidateOptionsResult.Fail("Auth:Schemes:LocalJwt:RefreshTokenDays must be between 1 and 90.");
            }

            if (options.Schemes.LocalJwt.AbsoluteSessionDays is < 1 or > 90)
            {
                return ValidateOptionsResult.Fail("Auth:Schemes:LocalJwt:AbsoluteSessionDays must be between 1 and 90.");
            }
            if (options.Schemes.LocalJwt.AccessTokenMinutes is < 1 or > 1440
                || options.Schemes.LocalJwt.AccessTokenMinutes > options.Schemes.LocalJwt.AbsoluteSessionDays * 1440)
                return ValidateOptionsResult.Fail("Auth:Schemes:LocalJwt:AccessTokenMinutes must be between 1 and 1440 and no longer than AbsoluteSessionDays.");
        }

        if (options.Schemes.Oidc.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Schemes.Oidc.Authority))
            {
                return ValidateOptionsResult.Fail("Auth:Schemes:Oidc:Authority is required when OIDC is enabled.");
            }

            if (string.IsNullOrWhiteSpace(options.Schemes.Oidc.Audience))
            {
                return ValidateOptionsResult.Fail("Auth:Schemes:Oidc:Audience is required when OIDC is enabled.");
            }

            if (string.IsNullOrWhiteSpace(options.Schemes.Oidc.Claims.Subject))
                return ValidateOptionsResult.Fail("Auth:Schemes:Oidc:Claims:Subject is required when OIDC is enabled.");

            if (!Uri.TryCreate(options.Schemes.Oidc.Authority, UriKind.Absolute, out var authority)
                || authority.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(authority.UserInfo)
                || !string.IsNullOrEmpty(authority.Query) || !string.IsNullOrEmpty(authority.Fragment))
            {
                return ValidateOptionsResult.Fail("Auth:Schemes:Oidc:Authority must be an absolute HTTPS URL.");
            }
        }

        if (options.Provisioning.Enabled && !options.Schemes.Oidc.Enabled)
        {
            return ValidateOptionsResult.Fail("Auth:Provisioning:Enabled requires Auth:Schemes:Oidc:Enabled=true.");
        }

        if (options.Provisioning.Enabled)
        {
            if (options.Provisioning.DefaultRole is not ("Admin" or "User"))
                return ValidateOptionsResult.Fail("Auth:Provisioning:DefaultRole must be Admin or User.");
            foreach (var rule in new[] { options.Provisioning.Admin, options.Provisioning.User })
            {
                if (rule.Emails.Any(string.IsNullOrWhiteSpace) || rule.Domains.Any(string.IsNullOrWhiteSpace)
                    || rule.Groups.Any(string.IsNullOrWhiteSpace) || rule.Roles.Any(string.IsNullOrWhiteSpace))
                    return ValidateOptionsResult.Fail("Auth:Provisioning:Admin/User rules cannot contain empty Emails, Domains, Groups or Roles.");
                if (rule.Emails.Any(email => !IsEmail(email)) || rule.Domains.Any(domain => !IsDomain(domain)))
                    return ValidateOptionsResult.Fail("Auth:Provisioning:Admin/User Emails or Domains are invalid.");
            }
        }

        if (options.Schemes.Oidc.Enabled && options.Schemes.Oidc.AllowedTenantIds.Any(string.IsNullOrWhiteSpace))
        {
            return ValidateOptionsResult.Fail("Auth:Schemes:Oidc:AllowedTenantIds cannot contain empty entries.");
        }

        if (options.Schemes.Oidc.Enabled && options.Schemes.Oidc.AllowedTenantIds.Count > 0 && string.IsNullOrWhiteSpace(options.Schemes.Oidc.Claims.TenantId))
        {
            return ValidateOptionsResult.Fail("Auth:Schemes:Oidc:Claims:TenantId is required when tenants are restricted.");
        }

        if (options.Provisioning.Enabled
            && (options.Schemes.Oidc.Claims.Subject != "sub"
                || string.IsNullOrWhiteSpace(options.Schemes.Oidc.Claims.Email)
                || string.IsNullOrWhiteSpace(options.Schemes.Oidc.Claims.EmailVerified)))
        {
            return ValidateOptionsResult.Fail("External provisioning requires the standard sub claim and configured Email/EmailVerified claim names.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsEmail(string email) => MailAddress.TryCreate(email, out var address)
        && address.Address == email && string.IsNullOrEmpty(address.DisplayName)
        && email.Count(c => c == '@') == 1 && IsDomain(email[(email.LastIndexOf('@') + 1)..]);

    private static bool IsDomain(string domain) => !string.IsNullOrWhiteSpace(domain)
        && Uri.CheckHostName(domain) == UriHostNameType.Dns
        && !domain.Any(char.IsWhiteSpace) && !domain.Contains('/') && !domain.Contains('@')
        && !domain.Contains(':') && !domain.Contains('?') && !domain.Contains('#');
}
