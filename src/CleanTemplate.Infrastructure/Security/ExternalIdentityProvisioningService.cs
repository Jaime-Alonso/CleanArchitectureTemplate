using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleanTemplate.Application.Abstractions;
using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Infrastructure.Security;

/// <summary>
/// Provisions and synchronizes an internal <see cref="ApplicationUser"/> from an external identity provider
/// (for example OIDC) and maps external roles to internal application roles.
/// </summary>
/// <remarks>
/// The service is idempotent for the same provider/subject and uses a short-lived in-memory cache to avoid
/// repeating provisioning work for repeated token transformations.
/// </remarks>
public sealed class ExternalIdentityProvisioningService : IExternalIdentityProvisioningService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IExternalRoleMapper _externalRoleMapper;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<ExternalIdentityProvisioningService> _logger;

    public ExternalIdentityProvisioningService(
        UserManager<ApplicationUser> userManager,
        IExternalRoleMapper externalRoleMapper,
        IMemoryCache memoryCache,
        ILogger<ExternalIdentityProvisioningService> logger)
    {
        _userManager = userManager;
        _externalRoleMapper = externalRoleMapper;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    /// <summary>
    /// Ensures the external identity is linked to an internal user, updates profile fields,
    /// associates external login information, and assigns mapped internal roles.
    /// </summary>
    /// <param name="externalIdentity">Identity data extracted from the external token/claims.</param>
    /// <param name="cancellationToken">Cancellation token for the provisioning operation.</param>
    /// <returns>
    /// A <see cref="ProvisionResult"/> containing the internal user identifier, resolved internal roles,
    /// and whether changes were applied during this execution.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when user creation, user update, login association, or role assignment fails.
    /// </exception>
    public async Task<ProvisionResult> ProvisionAsync(
        ExternalIdentity externalIdentity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = $"external-provisioning:{externalIdentity.Provider}:{externalIdentity.ProviderSubject}";
        if (_memoryCache.TryGetValue<ProvisionResult>(cacheKey, out var cachedResult) && cachedResult is not null)
        {
            return cachedResult;
        }

        var provisioningOccurred = false;
        var user = await _userManager.FindByLoginAsync(externalIdentity.Provider, externalIdentity.ProviderSubject).ConfigureAwait(false);

        if (user is null && !string.IsNullOrWhiteSpace(externalIdentity.Email))
        {
            user = await _userManager.FindByEmailAsync(externalIdentity.Email).ConfigureAwait(false);
        }

        if (user is null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = externalIdentity.Email,
                UserName = externalIdentity.Email ?? $"{externalIdentity.Provider}:{externalIdentity.ProviderSubject}",
                DisplayName = externalIdentity.Name,
                TenantId = externalIdentity.TenantId,
                EmailConfirmed = !string.IsNullOrWhiteSpace(externalIdentity.Email)
            };

            var createResult = await _userManager.CreateAsync(user).ConfigureAwait(false);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException($"Unable to create user from external identity: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
            }

            provisioningOccurred = true;
        }

        if (!string.Equals(user.DisplayName, externalIdentity.Name, StringComparison.Ordinal))
        {
            user.DisplayName = externalIdentity.Name;
            provisioningOccurred = true;
        }

        if (!string.Equals(user.TenantId, externalIdentity.TenantId, StringComparison.Ordinal))
        {
            user.TenantId = externalIdentity.TenantId;
            provisioningOccurred = true;
        }

        if (!string.IsNullOrWhiteSpace(externalIdentity.Email) && !string.Equals(user.Email, externalIdentity.Email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = externalIdentity.Email;
            user.UserName = externalIdentity.Email;
            provisioningOccurred = true;
        }

        if (provisioningOccurred)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var updateResult = await _userManager.UpdateAsync(user).ConfigureAwait(false);
            if (!updateResult.Succeeded)
            {
                throw new InvalidOperationException($"Unable to update user from external identity: {string.Join(", ", updateResult.Errors.Select(e => e.Description))}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var addLoginResult = await _userManager.AddLoginAsync(user, new UserLoginInfo(externalIdentity.Provider, externalIdentity.ProviderSubject, externalIdentity.Provider)).ConfigureAwait(false);
        if (!addLoginResult.Succeeded && addLoginResult.Errors.All(e => e.Code != "LoginAlreadyAssociated"))
        {
            throw new InvalidOperationException($"Unable to associate external login: {string.Join(", ", addLoginResult.Errors.Select(e => e.Description))}");
        }

        var targetRoles = _externalRoleMapper.MapRoles(externalIdentity);
        var existingRoles = await _userManager.GetRolesAsync(user).ConfigureAwait(false);

        foreach (var role in targetRoles.Except(existingRoles, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var addToRoleResult = await _userManager.AddToRoleAsync(user, role).ConfigureAwait(false);
            if (!addToRoleResult.Succeeded)
            {
                throw new InvalidOperationException($"Unable to assign role '{role}' to user '{user.Id}': {string.Join(", ", addToRoleResult.Errors.Select(e => e.Description))}");
            }

            provisioningOccurred = true;
        }

        var provisionResult = new ProvisionResult
        {
            InternalUserId = user.Id,
            InternalRoles = targetRoles,
            ProvisioningOccurred = provisioningOccurred
        };

        _memoryCache.Set(cacheKey, provisionResult, CacheDuration);

        _logger.LogInformation(
            "OIDC provisioning completed for provider {Provider} and subject {Subject}. Internal user {InternalUserId}. Provisioning occurred: {ProvisioningOccurred}",
            externalIdentity.Provider,
            externalIdentity.ProviderSubject,
            user.Id,
            provisioningOccurred);

        return provisionResult;
    }
}
