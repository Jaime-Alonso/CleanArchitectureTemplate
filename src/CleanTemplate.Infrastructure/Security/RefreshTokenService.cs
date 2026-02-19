using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CleanTemplate.Application.Abstractions;
using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Security;

public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly int _refreshTokenDays;

    public RefreshTokenService(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        IOptions<RefreshTokenOptions> options)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _refreshTokenDays = Math.Clamp(options.Value.RefreshTokenDays, 1, 90);
    }

    public async Task<RefreshTokenIssueResult> IssueAsync(
        Guid userId,
        string jwtId,
        string? createdByIp,
        CancellationToken cancellationToken = default)
    {
        var refreshToken = GenerateRefreshToken();
        var tokenHash = ComputeHash(refreshToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = now.AddDays(_refreshTokenDays);

        var entity = RefreshToken.Create(
            userId,
            jwtId,
            tokenHash,
            now,
            expiresAtUtc,
            createdByIp);

        await _dbContext.Set<RefreshToken>().AddAsync(entity, cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RefreshTokenIssueResult
        {
            RefreshToken = refreshToken,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    public async Task<RefreshTokenConsumptionResult?> ConsumeAsync(
        string refreshToken,
        string? consumedByIp,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = ComputeHash(refreshToken);

        var entity = await _dbContext.Set<RefreshToken>()
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!entity.CanBeConsumed(now))
        {
            return null;
        }

        entity.Revoke(now, consumedByIp);

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RefreshTokenConsumptionResult
        {
            UserId = entity.UserId
        };
    }

    public async Task<bool> RevokeAsync(
        string refreshToken,
        string? revokedByIp,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = ComputeHash(refreshToken);

        var entity = await _dbContext.Set<RefreshToken>()
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        if (entity.RevokedAtUtc is not null)
            return true;

        entity.Revoke(_timeProvider.GetUtcNow().UtcDateTime, revokedByIp);

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static string GenerateRefreshToken()
    {
        Span<byte> randomBytes = stackalloc byte[64];
        RandomNumberGenerator.Fill(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    private static string ComputeHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
