using CleanTemplate.Application.Abstractions;
using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace CleanTemplate.Infrastructure.Security;

public sealed class AuthUserService : IAuthUserService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly Dictionary<Guid, ApplicationUser> _usersById = [];
    private readonly Dictionary<string, ApplicationUser> _usersByLookup = new(StringComparer.OrdinalIgnoreCase);

    public AuthUserService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<AuthUser?> FindByEmailOrUserNameAsync(string emailOrUserName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryGetByLookup(emailOrUserName, out var cachedUser))
        {
            return Map(cachedUser);
        }

        var user = await _userManager.FindByEmailAsync(emailOrUserName).ConfigureAwait(false)
            ?? await _userManager.FindByNameAsync(emailOrUserName).ConfigureAwait(false);

        CacheLookup(emailOrUserName, user);

        return user is null ? null : Map(user);
    }

    public async Task<AuthUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await FindUserByIdAsync(userId).ConfigureAwait(false);
        return user is null ? null : Map(user);
    }

    public async Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await FindUserByIdAsync(userId).ConfigureAwait(false);
        return user is not null && await _userManager.IsLockedOutAsync(user).ConfigureAwait(false);
    }

    public async Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await FindUserByIdAsync(userId).ConfigureAwait(false);
        return user is not null && await _userManager.CheckPasswordAsync(user, password).ConfigureAwait(false);
    }

    public async Task AccessFailedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await FindUserByIdAsync(userId).ConfigureAwait(false);
        if (user is null)
        {
            return;
        }

        await _userManager.AccessFailedAsync(user).ConfigureAwait(false);
    }

    public async Task ResetAccessFailedCountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await FindUserByIdAsync(userId).ConfigureAwait(false);
        if (user is null)
        {
            return;
        }

        await _userManager.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await FindUserByIdAsync(userId).ConfigureAwait(false);
        if (user is null)
        {
            return [];
        }

        var roles = await _userManager.GetRolesAsync(user).ConfigureAwait(false);
        return roles.ToArray();
    }

    private async Task<ApplicationUser?> FindUserByIdAsync(Guid userId)
    {
        if (_usersById.TryGetValue(userId, out var cachedUser))
        {
            return cachedUser;
        }

        var user = await _userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        CacheUser(user);
        return user;
    }

    private bool TryGetByLookup(string emailOrUserName, out ApplicationUser user)
    {
        return _usersByLookup.TryGetValue(NormalizeLookup(emailOrUserName), out user!);
    }

    private void CacheLookup(string emailOrUserName, ApplicationUser? user)
    {
        if (user is null)
        {
            return;
        }

        _usersByLookup[NormalizeLookup(emailOrUserName)] = user;
        CacheUser(user);
    }

    private void CacheUser(ApplicationUser? user)
    {
        if (user is null)
        {
            return;
        }

        _usersById[user.Id] = user;

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            _usersByLookup[NormalizeLookup(user.Email)] = user;
        }

        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            _usersByLookup[NormalizeLookup(user.UserName)] = user;
        }
    }

    private static string NormalizeLookup(string value)
    {
        return value.Trim();
    }

    private static AuthUser Map(ApplicationUser user)
    {
        return new AuthUser
        {
            Id = user.Id,
            Email = user.Email
        };
    }
}
