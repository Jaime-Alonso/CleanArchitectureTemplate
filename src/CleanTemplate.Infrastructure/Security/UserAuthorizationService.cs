using CleanTemplate.Application.Contracts.Identity;
using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Security;

public sealed class UserAuthorizationService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<ExternalRoleMappingOptions> provisioningOptions) : IUserAuthorizationService
{
    public async Task<UserAuthorization?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var user = await dbContext.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.LockoutEnabled, user.LockoutEnd })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (user is null || (user.LockoutEnabled && user.LockoutEnd > now))
        {
            return null;
        }

        var manualRoles = await dbContext.UserRoles.AsNoTracking()
            .Where(assignment => assignment.UserId == userId)
            .Join(dbContext.Roles, assignment => assignment.RoleId, role => role.Id, (_, role) => role.Name!)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        List<string> externalRoles = [];
        if (provisioningOptions.Value.Enabled)
        {
            var nowUtc = now.UtcDateTime;
            externalRoles = await dbContext.Set<ExternalRoleGrant>().AsNoTracking()
                .Where(grant => grant.UserId == userId && grant.ValidUntilUtc > nowUtc
                    && dbContext.UserLogins.Any(login => login.UserId == grant.UserId
                        && login.LoginProvider == grant.LoginProvider && login.ProviderKey == grant.ProviderKey))
                .Join(dbContext.Roles, grant => grant.RoleId, role => role.Id, (_, role) => role.Name!)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        return new UserAuthorization(userId, manualRoles.Concat(externalRoles).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
}
