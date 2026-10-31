namespace CleanTemplate.Application.Security;

public sealed record UserAuthorization(Guid UserId, IReadOnlyCollection<string> Roles);
