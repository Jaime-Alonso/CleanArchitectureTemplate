using CleanTemplate.Application.Security;
using CleanTemplate.Core.SharedKernel.Results;
using Mediora;

namespace CleanTemplate.Application.Security.Commands.Login;

public sealed record LoginCommand : IRequest<Result<AuthSession>>
{
    public string EmailOrUserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string? ClientIp { get; init; }
}
