using CleanTemplate.Application.Security;
using CleanTemplate.Core.SharedKernel.Results;
using MediatR;

namespace CleanTemplate.Application.Security.Commands.RefreshToken;

public sealed record RefreshTokenCommand : IRequest<Result<AuthSession>>
{
    public string RefreshToken { get; init; } = string.Empty;
    public string? ClientIp { get; init; }
}
