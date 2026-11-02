using CleanTemplate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanTemplate.Infrastructure.Security;

public sealed class RefreshTokenCleanupService(ApplicationDbContext dbContext)
{
    public async Task<int> CleanupAsync(DateTime nowUtc, DateTime revokedThreshold, CancellationToken cancellationToken = default)
    {
        // Consumed ancestors remain until their family is no longer usable; deletion cascades to its tokens.
        var deleted = await dbContext.Set<RefreshTokenFamily>()
            .Where(family => family.ExpiresAtUtc <= nowUtc
                || (family.RevokedAtUtc.HasValue && family.RevokedAtUtc <= revokedThreshold))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        return deleted + await dbContext.Set<RefreshToken>()
            .Where(token => token.FamilyId == null && (token.ExpiresAtUtc <= nowUtc
                || (token.RevokedAtUtc.HasValue && token.RevokedAtUtc <= revokedThreshold)))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
