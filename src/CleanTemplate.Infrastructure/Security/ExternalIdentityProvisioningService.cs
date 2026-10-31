using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanTemplate.Application.Contracts;
using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Infrastructure.Security;

/// <summary>
/// Provisions identities from already validated tokens using exact issuer/subject keys.
/// Email is a verified profile attribute, never proof of ownership of an existing account.
/// </summary>
public sealed class ExternalIdentityProvisioningService(
    UserManager<ApplicationUser> userManager,
    IExternalRoleMapper externalRoleMapper,
    ILogger<ExternalIdentityProvisioningService> logger) : IExternalIdentityProvisioningService
{
    public async Task<Result<ProvisionResult>> ProvisionAsync(
        ExternalIdentity externalIdentity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        var existingRoles = await userManager.GetRolesAsync(user).ConfigureAwait(false);
        foreach (var role in targetRoles.Except(existingRoles, StringComparer.OrdinalIgnoreCase))
        {
            var result = await userManager.AddToRoleAsync(user, role).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("Unable to assign a configured internal role during external provisioning.");
            }

            provisioningOccurred = true;
        }

        return Result<ProvisionResult>.Success(new ProvisionResult
        {
            InternalUserId = user.Id,
            InternalRoles = targetRoles,
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
