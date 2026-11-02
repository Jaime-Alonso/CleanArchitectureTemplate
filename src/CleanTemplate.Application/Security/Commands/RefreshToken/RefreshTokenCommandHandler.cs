using CleanTemplate.Application.Contracts.Identity;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
using Mediora;
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
        var lookupResult = await _refreshTokenService.FindValidAsync(
            request.RefreshToken,
            request.ClientIp,
            cancellationToken).ConfigureAwait(false);

        if (lookupResult is null)
        {
            _logger.LogWarning("Refresh token rejected");
            return Result<AuthSession>.Failure(Error.Failure("Auth.InvalidRefreshToken", "Refresh token is invalid or expired."));
        }

        var user = await _authUserService.FindByIdAsync(lookupResult.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            await _refreshTokenService.RevokeAllForUserAsync(lookupResult.UserId, cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("Refresh token user not found: {UserId}", lookupResult.UserId);
            return Result<AuthSession>.Failure(Error.NotFound("Users.NotFound", $"User '{lookupResult.UserId}' was not found."));
        }

        if (await _authUserService.IsLockedOutAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            await _refreshTokenService.RevokeAllForUserAsync(user.Id, cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("Refresh token rejected for locked user {UserId}", user.Id);
            return Result<AuthSession>.Failure(Error.Failure("Auth.LockedOut", "User account is locked."));
        }

        var roles = await _authUserService.GetRolesAsync(user.Id, cancellationToken).ConfigureAwait(false);
        var token = _tokenGenerator.Generate(user, roles);
        var refreshResult = await _refreshTokenService.RotateAsync(
            request.RefreshToken,
            user.Id,
            token.JwtId,
            request.ClientIp,
            cancellationToken).ConfigureAwait(false);

        if (refreshResult is null)
        {
            _logger.LogWarning("Refresh token rotation rejected");
            return Result<AuthSession>.Failure(Error.Failure("Auth.InvalidRefreshToken", "Refresh token is invalid or expired."));
        }

        return Result<AuthSession>.Success(new AuthSession
        {
            AccessToken = token.AccessToken,
            RefreshToken = refreshResult.RefreshToken,
            AccessTokenExpiresAtUtc = token.ExpiresAtUtc,
            RefreshTokenExpiresAtUtc = refreshResult.ExpiresAtUtc
        });
    }
}
