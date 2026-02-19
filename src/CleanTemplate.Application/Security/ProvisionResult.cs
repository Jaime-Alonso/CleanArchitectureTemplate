namespace CleanTemplate.Application.Security;

public sealed record ProvisionResult
{
    public required Guid InternalUserId { get; init; }
    public required IReadOnlyCollection<string> InternalRoles { get; init; }
    public required bool ProvisioningOccurred { get; init; }
}
