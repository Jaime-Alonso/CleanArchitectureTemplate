using CleanTemplate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Identity;

public sealed class IdentityDataSeeder
{
    private static readonly string[] DefaultRoles = ["Admin", "User"];

    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IdentitySeedOptions _seedOptions;
    private readonly ApplicationDbContext _dbContext;

    public IdentityDataSeeder(
        RoleManager<IdentityRole<Guid>> roleManager,
        UserManager<ApplicationUser> userManager,
        IOptions<IdentitySeedOptions> seedOptions,
        ApplicationDbContext dbContext)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _seedOptions = seedOptions.Value;
        _dbContext = dbContext;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (_dbContext.Database.IsNpgsql())
        {
            await _dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7394126801321)", cancellationToken).ConfigureAwait(false);
        }
        else if (_dbContext.Database.IsSqlServer())
        {
            await _dbContext.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result = sp_getapplock @Resource = 'CleanTemplate.IdentityBootstrap', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; IF @result < 0 THROW 51000, 'Bootstrap lock unavailable', 1;", cancellationToken).ConfigureAwait(false);
        }
        foreach (var role in DefaultRoles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _roleManager.RoleExistsAsync(role).ConfigureAwait(false))
            {
                continue;
            }

            var creationResult = await _roleManager.CreateAsync(new IdentityRole<Guid>(role)).ConfigureAwait(false);
            if (!creationResult.Succeeded)
            {
                throw new InvalidOperationException($"Unable to create role '{role}': {string.Join(", ", creationResult.Errors.Select(e => e.Description))}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if ((await _userManager.GetUsersInRoleAsync("Admin").ConfigureAwait(false)).Count != 0)
        {
            throw new InvalidOperationException("An administrator already exists; bootstrap did not change any account.");
        }

        var adminEmail = _seedOptions.AdminEmail;
        var adminUser = await _userManager.FindByEmailAsync(adminEmail).ConfigureAwait(false);
        if (adminUser is not null)
        {
            throw new InvalidOperationException("Bootstrap account already exists; no roles or credentials were changed.");
        }
        if (adminUser is null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            adminUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = adminEmail,
                UserName = adminEmail,
                EmailConfirmed = true,
                DisplayName = "Template Admin"
            };

            var createUserResult = await _userManager.CreateAsync(adminUser, _seedOptions.AdminPassword).ConfigureAwait(false);
            if (!createUserResult.Succeeded)
            {
                throw new InvalidOperationException($"Unable to create default admin user: {string.Join(", ", createUserResult.Errors.Select(e => e.Description))}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!await _userManager.IsInRoleAsync(adminUser, "Admin").ConfigureAwait(false))
        {
            var addRoleResult = await _userManager.AddToRoleAsync(adminUser, "Admin").ConfigureAwait(false);
            if (!addRoleResult.Succeeded)
            {
                throw new InvalidOperationException($"Unable to assign Admin role to default admin user: {string.Join(", ", addRoleResult.Errors.Select(e => e.Description))}");
            }
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
