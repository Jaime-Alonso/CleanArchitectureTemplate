namespace CleanTemplate.Application.Security;

public sealed record RefreshTokenConsumptionResult
{
    public required Guid UserId { get; init; }
}
