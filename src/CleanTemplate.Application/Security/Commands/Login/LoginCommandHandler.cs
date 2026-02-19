using CleanTemplate.Application.Abstractions;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Application.Security.Commands.Login;

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, Result<AuthSession>>
{
    private readonly IAuthUserService _authUserService;
    private readonly ILocalJwtTokenGenerator _tokenGenerator;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        IAuthUserService authUserService,
        ILocalJwtTokenGenerator tokenGenerator,
        IRefreshTokenService refreshTokenService,
        ILogger<LoginCommandHandler> logger)
    {
        _authUserService = authUserService;
        _tokenGenerator = tokenGenerator;
        _refreshTokenService = refreshTokenService;
        _logger = logger;
    }

    public async Task<Result<AuthSession>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _authUserService.FindByEmailOrUserNameAsync(request.EmailOrUserName, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            _logger.LogWarning("Login failed for {EmailOrUserName}: user not found", request.EmailOrUserName);
            return Result<AuthSession>.Failure(Error.Failure("Auth.InvalidCredentials", "Invalid credentials."));
        }

        if (await _authUserService.IsLockedOutAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("Login blocked for user {UserId}: account locked out", user.Id);
            return Result<AuthSession>.Failure(Error.Failure("Auth.LockedOut", "User account is locked."));
        }

        var validPassword = await _authUserService.CheckPasswordAsync(user.Id, request.Password, cancellationToken).ConfigureAwait(false);
        if (!validPassword)
        {
            await _authUserService.AccessFailedAsync(user.Id, cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("Login failed for user {UserId}: invalid credentials", user.Id);
            return Result<AuthSession>.Failure(Error.Failure("Auth.InvalidCredentials", "Invalid credentials."));
        }

        await _authUserService.ResetAccessFailedCountAsync(user.Id, cancellationToken).ConfigureAwait(false);

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
