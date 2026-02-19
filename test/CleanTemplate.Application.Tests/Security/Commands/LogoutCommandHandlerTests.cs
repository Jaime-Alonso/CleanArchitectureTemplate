using CleanTemplate.Application.Abstractions;
using CleanTemplate.Application.Security;
using CleanTemplate.Application.Security.Commands.Logout;
using Microsoft.Extensions.Logging.Abstractions;

namespace CleanTemplate.Application.Tests.Security.Commands;

public sealed class LogoutCommandHandlerTests
{
    [Fact]
    public async Task Handle_WithRefreshToken_RevokesToken()
    {
        var refreshTokenService = new FakeRefreshTokenService();
        var handler = new LogoutCommandHandler(refreshTokenService, NullLogger<LogoutCommandHandler>.Instance);

        await handler.Handle(
            new LogoutCommand { RefreshToken = "refresh-token", ClientIp = "127.0.0.1" },
            CancellationToken.None);

        Assert.True(refreshTokenService.WasCalled);
        Assert.Equal("refresh-token", refreshTokenService.LastRefreshToken);
    }

    [Fact]
    public async Task Handle_WithoutRefreshToken_DoesNotCallRevoke()
    {
        var refreshTokenService = new FakeRefreshTokenService();
        var handler = new LogoutCommandHandler(refreshTokenService, NullLogger<LogoutCommandHandler>.Instance);

        await handler.Handle(new LogoutCommand { RefreshToken = string.Empty }, CancellationToken.None);

        Assert.False(refreshTokenService.WasCalled);
    }

    private sealed class FakeRefreshTokenService : IRefreshTokenService
    {
        public bool WasCalled { get; private set; }
        public string? LastRefreshToken { get; private set; }

        public Task<RefreshTokenIssueResult> IssueAsync(Guid userId, string jwtId, string? createdByIp, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<RefreshTokenConsumptionResult?> ConsumeAsync(string refreshToken, string? consumedByIp, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> RevokeAsync(string refreshToken, string? revokedByIp, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastRefreshToken = refreshToken;
            return Task.FromResult(true);
        }
    }
}
