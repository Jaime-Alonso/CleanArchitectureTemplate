using CleanTemplate.Application.Security;

namespace CleanTemplate.Application.Abstractions;

/// <summary>
/// Defines the contract for provisioning and synchronizing internal users from external identities.
/// </summary>
public interface IExternalIdentityProvisioningService
{
    /// <summary>
    /// Provisions or updates an internal user based on the external identity and returns the resolved internal context.
    /// </summary>
    /// <param name="externalIdentity">Identity data received from an external authentication provider.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The provisioning result containing internal user id, roles, and mutation status.</returns>
    Task<ProvisionResult> ProvisionAsync(
        ExternalIdentity externalIdentity,
        CancellationToken cancellationToken = default);
}
