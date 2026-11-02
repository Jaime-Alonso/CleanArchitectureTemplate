using CleanTemplate.Application.Security;
using CleanTemplate.SharedKernel.Results;

namespace CleanTemplate.Application.Contracts.Identity;

/// <summary>
/// Defines the contract for provisioning and synchronizing internal users from external identities.
/// </summary>
public interface IExternalIdentityProvisioningService
{
    /// <summary>
    /// Provisions or updates an internal user based on the external identity and returns the resolved internal context.
    /// </summary>
    /// <param name="externalIdentity">Identity data from an already validated token, including its exact issuer and subject.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The provisioning result containing internal user id, roles, and mutation status.</returns>
    Task<Result<ProvisionResult>> ProvisionAsync(
        ExternalIdentity externalIdentity,
        CancellationToken cancellationToken = default);
}
