using CleanTemplate.Application.Abstractions;
using Mediora;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Application.Security.Commands.Logout;

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ILogger<LogoutCommandHandler> _logger;

    public LogoutCommandHandler(
        IRefreshTokenService refreshTokenService,
        ILogger<LogoutCommandHandler> logger)
    {
        _refreshTokenService = refreshTokenService;
        _logger = logger;
    }

    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            _logger.LogInformation("Logout completed without token payload");
            return;
        }

        var revoked = await _refreshTokenService.RevokeAsync(
            request.RefreshToken,
            request.ClientIp,
            cancellationToken).ConfigureAwait(false);

        if (!revoked)
        {
            _logger.LogInformation("Logout completed for non-existent refresh token");
        }
    }
}
