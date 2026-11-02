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
    private readonly int _absoluteSessionDays;

    public RefreshTokenService(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        IOptions<RefreshTokenOptions> options)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _refreshTokenDays = Math.Clamp(options.Value.RefreshTokenDays, 1, 90);
        _absoluteSessionDays = Math.Clamp(options.Value.AbsoluteSessionDays, 1, 90);
    }

    public async Task<RefreshTokenIssueResult> IssueAsync(
        Guid userId,
        string jwtId,
        string? createdByIp,
        CancellationToken cancellationToken = default)
    {
        EnsureOwnTransaction();
        var refreshToken = GenerateRefreshToken();
        var tokenHash = ComputeHash(refreshToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = now.AddDays(_refreshTokenDays);
        var stamp = await ReadSecurityStampAsync(userId, cancellationToken).ConfigureAwait(false);
        var family = RefreshTokenFamily.Create(userId, now, now.AddDays(_absoluteSessionDays), stamp);
        expiresAtUtc = expiresAtUtc < family.ExpiresAtUtc ? expiresAtUtc : family.ExpiresAtUtc;

        var entity = RefreshToken.Create(
            userId,
            jwtId,
            tokenHash,
            now,
            expiresAtUtc,
            createdByIp, family.Id);

        _dbContext.Set<RefreshTokenFamily>().Add(family);
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
        EnsureOwnTransaction();
        var tokenHash = ComputeHash(refreshToken);

        var entity = await _dbContext.Set<RefreshToken>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        if (entity?.FamilyId is not Guid familyId)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (entity.ReplacedByTokenHash is not null)
        {
            await RevokeFamilyAsync(familyId, "ReuseDetected", now, cancellationToken).ConfigureAwait(false);
            return null;
        }
        if (!entity.CanBeConsumed(now))
        {
            return null;
        }

        if (!await _dbContext.Set<RefreshTokenFamily>().AnyAsync(
            x => x.Id == familyId && x.RevokedAtUtc == null && x.ExpiresAtUtc > now, cancellationToken).ConfigureAwait(false))
            return null;

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
        EnsureOwnTransaction();

        var tokenHash = ComputeHash(refreshToken);
        var replacement = GenerateRefreshToken();
        var replacementHash = ComputeHash(replacement);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var original = await _dbContext.Set<RefreshToken>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash && x.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (original?.FamilyId is not Guid familyId)
            return null;

        // Every rotation/revocation serializes on the family row before inspecting token state.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var locked = await _dbContext.Set<RefreshTokenFamily>()
            .Where(x => x.Id == familyId && x.UserId == userId && x.RevokedAtUtc == null && x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, x => x.Version + 1), cancellationToken).ConfigureAwait(false);
        if (locked != 1)
            return null;

        var family = await _dbContext.Set<RefreshTokenFamily>().AsNoTracking()
            .SingleAsync(x => x.Id == familyId, cancellationToken).ConfigureAwait(false);
        now = _timeProvider.GetUtcNow().UtcDateTime;
        if (family.ExpiresAtUtc <= now)
            return null;
        var stamp = await ReadSecurityStampAsync(userId, cancellationToken).ConfigureAwait(false);
        if (family.SecurityStamp != stamp)
        {
            await RevokeFamilyAsync(familyId, "SecurityStampChanged", now, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var expiresAtUtc = now.AddDays(_refreshTokenDays);
        var familyExpiresAtUtc = DateTime.SpecifyKind(family.ExpiresAtUtc, DateTimeKind.Utc);
        expiresAtUtc = expiresAtUtc < familyExpiresAtUtc ? expiresAtUtc : familyExpiresAtUtc;
        var entity = RefreshToken.Create(userId, jwtId, replacementHash, now, expiresAtUtc, createdByIp, familyId);
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
            // Reread after acquiring the family lock: a concurrent winner may just have consumed it.
            var replay = await _dbContext.Set<RefreshToken>().AsNoTracking()
                .AnyAsync(x => x.TokenHash == tokenHash && x.ReplacedByTokenHash != null, cancellationToken).ConfigureAwait(false);
            if (replay)
            {
                await RevokeFamilyAsync(familyId, "ReuseDetected", now, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
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

        EnsureOwnTransaction();
        var entity = await _dbContext.Set<RefreshToken>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        if (entity?.FamilyId is not Guid familyId)
        {
            return false;
        }

        await RevokeFamilyAsync(familyId, "Logout", _timeProvider.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        EnsureOwnTransaction();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await _dbContext.Set<RefreshTokenFamily>().Where(x => x.UserId == userId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, (DateTime?)now)
                .SetProperty(x => x.RevocationReason, "AccessWithdrawn"), cancellationToken).ConfigureAwait(false);
    }

    private Task<string?> ReadSecurityStampAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => x.SecurityStamp)
            .SingleOrDefaultAsync(cancellationToken);

    private Task<int> RevokeFamilyAsync(Guid familyId, string reason, DateTime now, CancellationToken cancellationToken) =>
        _dbContext.Set<RefreshTokenFamily>().Where(x => x.Id == familyId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, (DateTime?)now)
                .SetProperty(x => x.RevocationReason, reason), cancellationToken);

    private void EnsureOwnTransaction()
    {
        if (_dbContext.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Refresh token rotation requires its own transaction.");
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
