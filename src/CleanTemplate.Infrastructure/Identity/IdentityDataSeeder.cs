using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    public IdentityDataSeeder(
        RoleManager<IdentityRole<Guid>> roleManager,
        UserManager<ApplicationUser> userManager,
        IOptions<IdentitySeedOptions> seedOptions)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _seedOptions = seedOptions.Value;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
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

        var adminEmail = _seedOptions.AdminEmail;
        var adminUser = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == adminEmail, cancellationToken).ConfigureAwait(false);
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
    }
}
