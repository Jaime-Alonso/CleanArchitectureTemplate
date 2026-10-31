using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanTemplate.Application.Contracts;
using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Security;

/// <summary>
/// Provisions identities from already validated tokens using exact issuer/subject keys.
/// Email is a verified profile attribute, never proof of ownership of an existing account.
/// </summary>
public sealed class ExternalIdentityProvisioningService(
    UserManager<ApplicationUser> userManager,
    IExternalRoleMapper externalRoleMapper,
    ApplicationDbContext dbContext,
    IUserAuthorizationService authorizationService,
    IOptions<ExternalRoleMappingOptions> options,
    TimeProvider timeProvider,
    ILogger<ExternalIdentityProvisioningService> logger) : IExternalIdentityProvisioningService
{
    public async Task<Result<ProvisionResult>> ProvisionAsync(
        ExternalIdentity externalIdentity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();
        if (!options.Value.Enabled || externalIdentity.TokenExpiresAtUtc <= now)
        {
            return Reject("Auth.ExternalProvisioningUnavailable");
        }

        if (string.IsNullOrWhiteSpace(externalIdentity.Issuer) || string.IsNullOrWhiteSpace(externalIdentity.ProviderSubject))
        {
            return Reject("Auth.InvalidExternalIdentity");
        }

        // Identity requires unique email in this template. Do not infer verification from its presence.
        if (!externalIdentity.EmailVerified || string.IsNullOrWhiteSpace(externalIdentity.Email))
        {
            return Reject("Auth.ExternalEmailNotVerified");
        }

        // Hash exact values (no case folding/URL normalization) to fit Identity's bounded login keys.
        // Provider is only a display label: changing it must not change ownership.
        var loginProvider = $"oidc:{Hash(externalIdentity.Issuer)}";
        var providerKey = Hash(externalIdentity.ProviderSubject);
        var user = await userManager.FindByLoginAsync(loginProvider, providerKey).ConfigureAwait(false);
        if (user is not null && user.LockoutEnabled && user.LockoutEnd > now)
        {
            return Reject("Auth.ExternalAccessDenied");
        }

        var emailOwner = await userManager.FindByEmailAsync(externalIdentity.Email).ConfigureAwait(false);
        if (emailOwner is not null && emailOwner.Id != user?.Id)
        {
            // Even a verified email cannot automatically link a local account or another external login.
            return Reject("Auth.ExternalAccountLinkRequired");
        }

        var provisioningOccurred = false;
        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = $"external-{Hash(JsonSerializer.Serialize(new[] { externalIdentity.Issuer, externalIdentity.ProviderSubject }))}",
                Email = externalIdentity.Email,
                EmailConfirmed = true,
                DisplayName = externalIdentity.Name,
                TenantId = externalIdentity.TenantId
            };

            var createResult = await userManager.CreateAsync(user).ConfigureAwait(false);
            if (!createResult.Succeeded)
            {
                return Reject("Auth.ExternalProfileRejected");
            }

            var loginResult = await userManager.AddLoginAsync(user, new UserLoginInfo(
                loginProvider, providerKey, externalIdentity.Provider)).ConfigureAwait(false);
            if (!loginResult.Succeeded)
            {
                return Reject("Auth.ExternalLoginRejected");
            }

            provisioningOccurred = true;
        }
        else
        {
            if (!string.Equals(user.TenantId, externalIdentity.TenantId, StringComparison.Ordinal))
            {
                return Reject("Auth.ExternalTenantChanged");
            }

            if (!string.Equals(user.Email, externalIdentity.Email, StringComparison.Ordinal)
                || !string.Equals(user.DisplayName, externalIdentity.Name, StringComparison.Ordinal)
                || !user.EmailConfirmed)
            {
                user.Email = externalIdentity.Email;
                user.EmailConfirmed = true;
                user.DisplayName = externalIdentity.Name;
                // Keep the stable external username; profile changes never change account identity.
                var updateResult = await userManager.UpdateAsync(user).ConfigureAwait(false);
                if (!updateResult.Succeeded)
                {
                    return Reject("Auth.ExternalProfileRejected");
                }

                provisioningOccurred = true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var targetRoles = externalRoleMapper.MapRoles(externalIdentity);
        var normalizedRoles = targetRoles.Select(role => userManager.NormalizeName(role)).Distinct().ToArray();
        var roles = await dbContext.Roles.Where(role => normalizedRoles.Contains(role.NormalizedName))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (roles.Count != normalizedRoles.Length)
        {
            throw new InvalidOperationException("An external role mapping references an unknown internal role.");
        }

        var grants = await dbContext.Set<ExternalRoleGrant>()
            .Where(grant => grant.UserId == user.Id && grant.LoginProvider == loginProvider && grant.ProviderKey == providerKey)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var validUntil = now.AddMinutes(options.Value.RoleGrantLifetimeMinutes);
        if (externalIdentity.TokenExpiresAtUtc < validUntil)
        {
            validUntil = externalIdentity.TokenExpiresAtUtc;
        }

        foreach (var grant in grants.Where(grant => roles.All(role => role.Id != grant.RoleId)))
        {
            dbContext.Remove(grant);
            provisioningOccurred = true;
        }

        foreach (var role in roles)
        {
            var grant = grants.SingleOrDefault(grant => grant.RoleId == role.Id);
            if (grant is null)
            {
                dbContext.Add(new ExternalRoleGrant(user.Id, loginProvider, providerKey, role.Id, validUntil.UtcDateTime));
                provisioningOccurred = true;
            }
            else if (grant.ValidUntilUtc != validUntil.UtcDateTime)
            {
                grant.SetExpiration(validUntil.UtcDateTime);
                provisioningOccurred = true;
            }
        }

        // EF commits this source's removals/additions/renewals atomically in a single SaveChanges.
        // UserRoles remains exclusively manual; first-user provisioning concurrency is still T-08.
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var authorization = await authorizationService.GetAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (authorization is null)
        {
            return Reject("Auth.ExternalAccessDenied");
        }

        return Result<ProvisionResult>.Success(new ProvisionResult
        {
            InternalUserId = user.Id,
            InternalRoles = authorization.Roles,
            ProvisioningOccurred = provisioningOccurred
        });
    }

    private Result<ProvisionResult> Reject(string code)
    {
        logger.LogWarning("External provisioning rejected. Code {Code}", code);
        return Result<ProvisionResult>.Failure(new Error(code, "External identity cannot be provisioned.", ErrorType.Forbidden));
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
