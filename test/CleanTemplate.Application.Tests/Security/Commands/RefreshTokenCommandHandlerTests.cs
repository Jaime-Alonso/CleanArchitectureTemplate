using CleanTemplate.Application.Contracts;
using CleanTemplate.Application.Security;
using CleanTemplate.Application.Security.Commands.RefreshToken;
using Microsoft.Extensions.Logging.Abstractions;

namespace CleanTemplate.Application.Tests.Security.Commands;

public sealed class RefreshTokenCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenRefreshTokenIsValid_ReturnsNewSession()
    {
        var userId = Guid.NewGuid();
        var authUserService = new FakeAuthUserService
        {
            User = new AuthUser { Id = userId, Email = "user@local.template" },
            IsLockedOutResult = false,
            Roles = ["User"]
        };

        var refreshTokenService = new FakeRefreshTokenService
        {
            ConsumeResult = new RefreshTokenConsumptionResult { UserId = userId }
        };

        var handler = new RefreshTokenCommandHandler(
            authUserService,
            new FakeTokenGenerator(),
            refreshTokenService,
            NullLogger<RefreshTokenCommandHandler>.Instance);

        var result = await handler.Handle(
            new RefreshTokenCommand { RefreshToken = "valid-refresh-token", ClientIp = "127.0.0.1" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("access-token", result.Value.AccessToken);
        Assert.Equal("new-refresh-token", result.Value.RefreshToken);
        Assert.Equal(("valid-refresh-token", userId, "jwt-id", "127.0.0.1"), refreshTokenService.LastRotation);
        Assert.Equal(0, refreshTokenService.IssueCalls);
    }

    [Fact]
    public async Task Handle_WhenRefreshTokenIsRejected_ReturnsFailure()
    {
        var handler = new RefreshTokenCommandHandler(
            new FakeAuthUserService(),
            new FakeTokenGenerator(),
            new FakeRefreshTokenService(),
            NullLogger<RefreshTokenCommandHandler>.Instance);

        var result = await handler.Handle(
            new RefreshTokenCommand { RefreshToken = "invalid" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "Auth.InvalidRefreshToken");
    }

    [Fact]
    public async Task Handle_WhenRotationLosesRace_DoesNotReturnGeneratedTokens()
    {
        var userId = Guid.NewGuid();
        var refresh = new FakeRefreshTokenService
        {
            ConsumeResult = new RefreshTokenConsumptionResult { UserId = userId },
            RejectRotation = true
        };
        var handler = new RefreshTokenCommandHandler(
            new FakeAuthUserService { User = new AuthUser { Id = userId, Email = "user@example.test" } },
            new FakeTokenGenerator(), refresh, NullLogger<RefreshTokenCommandHandler>.Instance);

        var result = await handler.Handle(new RefreshTokenCommand { RefreshToken = "raced-token" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "Auth.InvalidRefreshToken");
        Assert.True(refresh.RotationCalled);
        Assert.Equal(0, refresh.IssueCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenUserIsMissingOrLocked_DoesNotRotate(bool locked)
    {
        var userId = Guid.NewGuid();
        var refresh = new FakeRefreshTokenService
        {
            ConsumeResult = new RefreshTokenConsumptionResult { UserId = userId }
        };
        var handler = new RefreshTokenCommandHandler(
            new FakeAuthUserService
            {
                User = locked ? new AuthUser { Id = userId, Email = "user@example.test" } : null,
                IsLockedOutResult = locked
            },
            new FakeTokenGenerator(), refresh, NullLogger<RefreshTokenCommandHandler>.Instance);

        var result = await handler.Handle(new RefreshTokenCommand { RefreshToken = "valid-token" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.False(refresh.RotationCalled);
        Assert.Contains(result.Errors, error => error.Code == (locked ? "Auth.LockedOut" : "Users.NotFound"));
    }

    [Fact]
    public async Task Handle_WhenAccessTokenGenerationFails_DoesNotConsumeRefreshToken()
    {
        var userId = Guid.NewGuid();
        var refresh = new FakeRefreshTokenService { ConsumeResult = new RefreshTokenConsumptionResult { UserId = userId } };
        var handler = new RefreshTokenCommandHandler(
            new FakeAuthUserService { User = new AuthUser { Id = userId, Email = "user@example.test" } },
            new FakeTokenGenerator { Fail = true }, refresh, NullLogger<RefreshTokenCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new RefreshTokenCommand { RefreshToken = "valid-token" }, CancellationToken.None));

        Assert.False(refresh.RotationCalled);
        Assert.Equal(0, refresh.IssueCalls);
    }

    private sealed class FakeAuthUserService : IAuthUserService
    {
        public AuthUser? User { get; set; }
        public bool IsLockedOutResult { get; set; }
        public IReadOnlyCollection<string> Roles { get; set; } = [];

        public Task<AuthUser?> FindByEmailOrUserNameAsync(string emailOrUserName, CancellationToken cancellationToken = default)
            => Task.FromResult(User);

        public Task<AuthUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(User);

        public Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(IsLockedOutResult);

        public Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task AccessFailedAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ResetAccessFailedCountAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Roles);
    }

    private sealed class FakeTokenGenerator : ILocalJwtTokenGenerator
    {
        public bool Fail { get; init; }

        public LocalTokenResult Generate(AuthUser user, IReadOnlyCollection<string> roles)
        {
            if (Fail)
                throw new InvalidOperationException("Injected signing failure.");

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
        public RefreshTokenConsumptionResult? ConsumeResult { get; set; }
        public bool RejectRotation { get; set; }
        public bool RotationCalled { get; private set; }
        public (string Token, Guid UserId, string JwtId, string? Ip)? LastRotation { get; private set; }
        public int IssueCalls { get; private set; }

        public Task<RefreshTokenIssueResult> IssueAsync(Guid userId, string jwtId, string? createdByIp, CancellationToken cancellationToken = default)
        {
            IssueCalls++;
            return Task.FromResult(new RefreshTokenIssueResult
            {
                RefreshToken = "new-refresh-token",
                ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
            });
        }

        public Task<RefreshTokenIssueResult?> RotateAsync(string refreshToken, Guid userId, string jwtId, string? createdByIp, CancellationToken cancellationToken = default)
        {
            RotationCalled = true;
            LastRotation = (refreshToken, userId, jwtId, createdByIp);
            return Task.FromResult<RefreshTokenIssueResult?>(RejectRotation ? null : new RefreshTokenIssueResult
            {
                RefreshToken = "new-refresh-token",
                ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
            });
        }

        public Task<RefreshTokenConsumptionResult?> FindValidAsync(string refreshToken, string? consumedByIp, CancellationToken cancellationToken = default)
            => Task.FromResult(ConsumeResult);

        public Task<bool> RevokeAsync(string refreshToken, string? revokedByIp, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
