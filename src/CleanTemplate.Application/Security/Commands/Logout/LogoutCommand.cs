using MediatR;

namespace CleanTemplate.Application.Security.Commands.Logout;

public sealed record LogoutCommand : IRequest
{
    public string RefreshToken { get; init; } = string.Empty;
    public string? ClientIp { get; init; }
}
