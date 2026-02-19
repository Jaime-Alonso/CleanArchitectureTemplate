namespace CleanTemplate.Application.Security;

public sealed record AuthUser
{
    public required Guid Id { get; init; }
    public string? Email { get; init; }
}
