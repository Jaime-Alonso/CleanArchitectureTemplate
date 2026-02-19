using CleanTemplate.Application.Abstractions;
using CleanTemplate.Application.Security;
using CleanTemplate.Application.Security.Commands.Login;
using Microsoft.Extensions.Logging.Abstractions;

namespace CleanTemplate.Application.Tests.Security.Commands;

public sealed class LoginCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCredentialsAreValid_ReturnsAuthSession()
    {
        var authUserService = new FakeAuthUserService
        {
            User = new AuthUser { Id = Guid.NewGuid(), Email = "admin@local.template" },
            CheckPasswordResult = true,
            IsLockedOutResult = false,
            Roles = ["Admin"]
        };

        var tokenGenerator = new FakeTokenGenerator();
        var refreshTokenService = new FakeRefreshTokenService();

        var handler = new LoginCommandHandler(
            authUserService,
            tokenGenerator,
            refreshTokenService,
            NullLogger<LoginCommandHandler>.Instance);

        var command = new LoginCommand
        {
            EmailOrUserName = "admin@local.template",
            Password = "ValidPassword123!",
            ClientIp = "127.0.0.1"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("access-token", result.Value.AccessToken);
        Assert.Equal("refresh-token", result.Value.RefreshToken);
    }

    [Fact]
    public async Task Handle_WhenUserDoesNotExist_ReturnsFailure()
    {
        var handler = new LoginCommandHandler(
            new FakeAuthUserService(),
            new FakeTokenGenerator(),
            new FakeRefreshTokenService(),
            NullLogger<LoginCommandHandler>.Instance);

        var result = await handler.Handle(
            new LoginCommand { EmailOrUserName = "missing@local.template", Password = "x" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "Auth.InvalidCredentials");
    }

    private sealed class FakeAuthUserService : IAuthUserService
    {
        public AuthUser? User { get; set; }
        public bool IsLockedOutResult { get; set; }
        public bool CheckPasswordResult { get; set; }
        public IReadOnlyCollection<string> Roles { get; set; } = [];

        public Task<AuthUser?> FindByEmailOrUserNameAsync(string emailOrUserName, CancellationToken cancellationToken = default)
            => Task.FromResult(User);

        public Task<AuthUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(User);

        public Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(IsLockedOutResult);

        public Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default)
            => Task.FromResult(CheckPasswordResult);

        public Task AccessFailedAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ResetAccessFailedCountAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Roles);
    }

    private sealed class FakeTokenGenerator : ILocalJwtTokenGenerator
    {
        public LocalTokenResult Generate(AuthUser user, IReadOnlyCollection<string> roles)
        {
            return new LocalTokenResult
            {
                AccessToken = "access-token",
                JwtId = "jwt-id",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
            };
        }
    }

    private sealed class FakeRefreshTokenService : IRefreshTokenService
    {
        public Task<RefreshTokenIssueResult> IssueAsync(Guid userId, string jwtId, string? createdByIp, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RefreshTokenIssueResult
            {
                RefreshToken = "refresh-token",
                ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
            });
        }

        public Task<RefreshTokenConsumptionResult?> ConsumeAsync(string refreshToken, string? consumedByIp, CancellationToken cancellationToken = default)
            => Task.FromResult<RefreshTokenConsumptionResult?>(null);

        public Task<bool> RevokeAsync(string refreshToken, string? revokedByIp, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
