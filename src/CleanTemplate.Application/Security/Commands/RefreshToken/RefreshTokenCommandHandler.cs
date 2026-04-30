using CleanTemplate.Application.Abstractions;
using CleanTemplate.Core.SharedKernel.Errors;
using CleanTemplate.Core.SharedKernel.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Application.Security.Commands.RefreshToken;

public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<AuthSession>>
{
    private readonly IAuthUserService _authUserService;
    private readonly ILocalJwtTokenGenerator _tokenGenerator;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        IAuthUserService authUserService,
        ILocalJwtTokenGenerator tokenGenerator,
        IRefreshTokenService refreshTokenService,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _authUserService = authUserService;
        _tokenGenerator = tokenGenerator;
        _refreshTokenService = refreshTokenService;
        _logger = logger;
    }

    public async Task<Result<AuthSession>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var consumeResult = await _refreshTokenService.ConsumeAsync(
            request.RefreshToken,
            request.ClientIp,
            cancellationToken).ConfigureAwait(false);

        if (consumeResult is null)
        {
            _logger.LogWarning("Refresh token rejected");
            return Result<AuthSession>.Failure(Error.Failure("Auth.InvalidRefreshToken", "Refresh token is invalid or expired."));
        }

        var user = await _authUserService.FindByIdAsync(consumeResult.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            _logger.LogWarning("Refresh token user not found: {UserId}", consumeResult.UserId);
            return Result<AuthSession>.Failure(Error.NotFound("Users.NotFound", $"User '{consumeResult.UserId}' was not found."));
        }

        if (await _authUserService.IsLockedOutAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("Refresh token rejected for locked user {UserId}", user.Id);
            return Result<AuthSession>.Failure(Error.Failure("Auth.LockedOut", "User account is locked."));
        }

        var roles = await _authUserService.GetRolesAsync(user.Id, cancellationToken).ConfigureAwait(false);
        var token = _tokenGenerator.Generate(user, roles);
        var refreshResult = await _refreshTokenService.IssueAsync(
            user.Id,
            token.JwtId,
            request.ClientIp,
            cancellationToken).ConfigureAwait(false);

        return Result<AuthSession>.Success(new AuthSession
        {
            AccessToken = token.AccessToken,
            RefreshToken = refreshResult.RefreshToken,
            AccessTokenExpiresAtUtc = token.ExpiresAtUtc,
            RefreshTokenExpiresAtUtc = refreshResult.ExpiresAtUtc
        });
    }
}
