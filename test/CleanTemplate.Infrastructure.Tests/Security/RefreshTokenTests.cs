using CleanTemplate.Infrastructure.Security;

namespace CleanTemplate.Infrastructure.Tests.Security;

public sealed class RefreshTokenTests
{
    [Fact]
    public void CanBeConsumed_WhenTokenIsActiveAndNotExpired_ReturnsTrue()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var token = RefreshToken.Create(
            Guid.NewGuid(),
            "jwt-id",
            "hash",
            now,
            now.AddDays(1),
            "127.0.0.1");

        Assert.True(token.CanBeConsumed(now.AddHours(1)));
    }

    [Fact]
    public void Revoke_WhenCalledTwice_ReturnsFalseOnSecondCall()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var token = RefreshToken.Create(
            Guid.NewGuid(),
            "jwt-id",
            "hash",
            now,
            now.AddDays(1),
            "127.0.0.1");

        var first = token.Revoke(now.AddHours(1), "10.0.0.1", "replacement-hash");
        var second = token.Revoke(now.AddHours(2), "10.0.0.2");

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(now.AddHours(1), token.RevokedAtUtc);
        Assert.Equal("10.0.0.1", token.RevokedByIp);
        Assert.Equal("replacement-hash", token.ReplacedByTokenHash);
    }
}
