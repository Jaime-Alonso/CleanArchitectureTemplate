using System.Security.Cryptography;
using System.Text;
using CleanTemplate.Application.Contracts;
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

    public async Task<RefreshTokenConsumptionResult?> FindValidAsync(
        string refreshToken,
        string? consumedByIp,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = ComputeHash(refreshToken);

        var entity = await _dbContext.Set<RefreshToken>()
            .AsNoTracking()
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

        return new RefreshTokenConsumptionResult
        {
            UserId = entity.UserId
        };
    }

    public async Task<RefreshTokenIssueResult?> RotateAsync(
        string refreshToken,
        Guid userId,
        string jwtId,
        string? createdByIp,
        CancellationToken cancellationToken = default)
    {
        // A returned session must not depend on an outer transaction's later commit.
        if (_dbContext.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
        {
            throw new InvalidOperationException("Refresh token rotation requires its own transaction.");
        }

        var tokenHash = ComputeHash(refreshToken);
        var replacement = GenerateRefreshToken();
        var replacementHash = ComputeHash(replacement);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = now.AddDays(_refreshTokenDays);
        var entity = RefreshToken.Create(userId, jwtId, replacementHash, now, expiresAtUtc, createdByIp);

        // The conditional write takes the database lock before any read: no process-local
        // synchronization or read-then-update race, including across application instances.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var affected = await _dbContext.Set<RefreshToken>()
            .Where(x => x.TokenHash == tokenHash && x.UserId == userId &&
                x.RevokedAtUtc == null && x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAtUtc, (DateTime?)now)
                .SetProperty(x => x.RevokedByIp, createdByIp)
                .SetProperty(x => x.ReplacedByTokenHash, replacementHash), cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return null;
        }

        _dbContext.Set<RefreshToken>().Add(entity);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Do not leave an insert queued for an unrelated later SaveChanges.
            _dbContext.Entry(entity).State = EntityState.Detached;
            throw;
        }

        return new RefreshTokenIssueResult { RefreshToken = replacement, ExpiresAtUtc = expiresAtUtc };
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
