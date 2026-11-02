using System.Data.Common;
using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Security;
using CleanTemplate.Infrastructure.Tests.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Tests.Security;

public sealed class RoleSynchronizationTests
{
    [Fact]
    public async Task ExternalRoles_AreStoredSeparatelyAndSharedWithLocalAuthorization()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync(Mapping());

        var result = await fixture.Provisioner.ProvisionAsync(Identity(fixture, admin: true));

        Assert.True(result.IsSuccess);
        Assert.Equal(["Admin"], result.Value.InternalRoles);
        Assert.Empty(await fixture.Db.UserRoles.ToListAsync());
        Assert.Single(await fixture.Db.Set<ExternalRoleGrant>().ToListAsync());
        Assert.Equal(result.Value.InternalRoles, await fixture.AuthUsers.GetRolesAsync(result.Value.InternalUserId));
    }

    [Fact]
    public async Task ExternalDemotion_RemovesObsoleteGrantInsteadOfLeavingPersistentAdmin()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync(Mapping());
        var initial = await fixture.Provisioner.ProvisionAsync(Identity(fixture, admin: true));
        Assert.True(initial.IsSuccess);

        var demoted = await fixture.Provisioner.ProvisionAsync(Identity(fixture));

        Assert.True(demoted.IsSuccess);
        Assert.Equal(["User"], demoted.Value.InternalRoles);
        Assert.Equal(["User"], await fixture.AuthUsers.GetRolesAsync(initial.Value.InternalUserId));
        var grant = Assert.Single(await fixture.Db.Set<ExternalRoleGrant>().ToListAsync());
        Assert.Equal("User", (await fixture.Db.Roles.SingleAsync(role => role.Id == grant.RoleId)).Name);
        Assert.Empty(await fixture.Db.UserRoles.ToListAsync());
    }

    [Fact]
    public async Task OverlappingManualAdmin_SurvivesExternalDemotionUntilExplicitlyRemoved()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync(Mapping());
        var initial = await fixture.Provisioner.ProvisionAsync(Identity(fixture, admin: true));
        Assert.True(initial.IsSuccess);
        var user = await fixture.Users.FindByIdAsync(initial.Value.InternalUserId.ToString());
        Assert.NotNull(user);
        Assert.True((await fixture.Users.AddToRoleAsync(user, "Admin")).Succeeded);

        var demoted = await fixture.Provisioner.ProvisionAsync(Identity(fixture));

        Assert.True(demoted.IsSuccess);
        Assert.Equal(["Admin", "User"], demoted.Value.InternalRoles);
        Assert.Equal(["Admin"], await fixture.Users.GetRolesAsync(user));
        Assert.Equal(demoted.Value.InternalRoles, await fixture.AuthUsers.GetRolesAsync(user.Id));

        Assert.True((await fixture.Users.RemoveFromRoleAsync(user, "Admin")).Succeeded);
        Assert.Equal(["User"], await fixture.AuthUsers.GetRolesAsync(user.Id));
    }

    [Fact]
    public async Task Synchronization_OnlyReplacesGrantsOwnedByTheCurrentLogin()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync(Mapping());
        var initial = await fixture.Provisioner.ProvisionAsync(Identity(fixture, admin: true));
        Assert.True(initial.IsSuccess);
        var user = await fixture.Users.FindByIdAsync(initial.Value.InternalUserId.ToString());
        Assert.NotNull(user);
        Assert.True((await fixture.Users.AddLoginAsync(user, new UserLoginInfo("another-provider", "another-subject", "Another"))).Succeeded);
        var adminRole = await fixture.Db.Roles.SingleAsync(role => role.Name == "Admin");
        fixture.Db.Add(new ExternalRoleGrant(user.Id, "another-provider", "another-subject", adminRole.Id,
            fixture.Clock.GetUtcNow().AddMinutes(3).UtcDateTime));
        await fixture.Db.SaveChangesAsync();

        var demoted = await fixture.Provisioner.ProvisionAsync(Identity(fixture));

        Assert.True(demoted.IsSuccess);
        Assert.Equal(["Admin", "User"], demoted.Value.InternalRoles);
        Assert.Single(await fixture.Db.Set<ExternalRoleGrant>().Where(grant => grant.LoginProvider == "another-provider").ToListAsync());
        Assert.Equal(2, await fixture.Db.Set<ExternalRoleGrant>().CountAsync());
    }

    [Fact]
    public async Task GrantsExpireAtTheConfiguredBoundary_WithoutRemovingManualRoles()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync(Mapping());
        var initial = await fixture.Provisioner.ProvisionAsync(Identity(fixture, admin: true));
        Assert.True(initial.IsSuccess);
        var user = await fixture.Users.FindByIdAsync(initial.Value.InternalUserId.ToString());
        Assert.NotNull(user);
        Assert.True((await fixture.Users.AddToRoleAsync(user, "User")).Succeeded);

        fixture.Clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));
        Assert.Equal(["Admin", "User"], await fixture.AuthUsers.GetRolesAsync(user.Id));
        fixture.Clock.Advance(TimeSpan.FromTicks(1));

        Assert.Equal(["User"], await fixture.AuthUsers.GetRolesAsync(user.Id));
        Assert.Single(await fixture.Db.Set<ExternalRoleGrant>().ToListAsync()); // Expiry needs no cleanup job to revoke access.
        Assert.Equal(["User"], await fixture.Users.GetRolesAsync(user));
    }

    [Fact]
    public async Task RenewingGrants_NeverExtendsThemBeyondTheOriginalTokenExpiration()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync(Mapping());
        var identity = Identity(fixture, admin: true) with { TokenExpiresAtUtc = fixture.Clock.GetUtcNow().AddMinutes(2) };
        var initial = await fixture.Provisioner.ProvisionAsync(identity);
        Assert.True(initial.IsSuccess);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await fixture.Provisioner.ProvisionAsync(identity)).IsSuccess);
        Assert.Equal(identity.TokenExpiresAtUtc.UtcDateTime,
            (await fixture.Db.Set<ExternalRoleGrant>().SingleAsync()).ValidUntilUtc);

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Empty(await fixture.AuthUsers.GetRolesAsync(initial.Value.InternalUserId));
        Assert.True((await fixture.Provisioner.ProvisionAsync(identity)).IsFailure);
    }

    [Fact]
    public async Task RemovingTheExternalLogin_ImmediatelyExcludesItsGrants()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync(Mapping());
        var initial = await fixture.Provisioner.ProvisionAsync(Identity(fixture, admin: true));
        Assert.True(initial.IsSuccess);
        var user = await fixture.Users.FindByIdAsync(initial.Value.InternalUserId.ToString());
        Assert.NotNull(user);
        var login = Assert.Single(await fixture.Users.GetLoginsAsync(user));

        Assert.True((await fixture.Users.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey)).Succeeded);

        Assert.Empty(await fixture.AuthUsers.GetRolesAsync(user.Id));
        Assert.Single(await fixture.Db.Set<ExternalRoleGrant>().ToListAsync());
    }

    [Fact]
    public async Task LockedUsers_ReceiveNoPermissionsAndCannotRenewExternalGrants()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync(Mapping());
        var initial = await fixture.Provisioner.ProvisionAsync(Identity(fixture, admin: true));
        Assert.True(initial.IsSuccess);
        var user = await fixture.Users.FindByIdAsync(initial.Value.InternalUserId.ToString());
        Assert.NotNull(user);
        Assert.True((await fixture.Users.SetLockoutEnabledAsync(user, true)).Succeeded);
        Assert.True((await fixture.Users.SetLockoutEndDateAsync(user, fixture.Clock.GetUtcNow().AddHours(1))).Succeeded);

        Assert.Null(await fixture.Authorization.GetAsync(user.Id));
        Assert.Empty(await fixture.AuthUsers.GetRolesAsync(user.Id));
        var rejected = await fixture.Provisioner.ProvisionAsync(Identity(fixture, admin: true));
        Assert.True(rejected.IsFailure);
        Assert.Equal("Auth.ExternalAccessDenied", rejected.FirstError?.Code);
    }

    [Fact]
    public async Task LockoutExpiration_UsesTheSameClockInLocalAndExternalAuthorization()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync(Mapping());
        var initial = await fixture.Provisioner.ProvisionAsync(Identity(fixture, admin: true));
        Assert.True(initial.IsSuccess);
        var user = await fixture.Users.FindByIdAsync(initial.Value.InternalUserId.ToString());
        Assert.NotNull(user);
        Assert.True((await fixture.Users.SetLockoutEnabledAsync(user, true)).Succeeded);
        Assert.True((await fixture.Users.SetLockoutEndDateAsync(user, fixture.Clock.GetUtcNow().AddMinutes(1))).Succeeded);
        Assert.Null(await fixture.Authorization.GetAsync(user.Id));

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(["Admin"], await fixture.AuthUsers.GetRolesAsync(user.Id));
        Assert.True((await fixture.Provisioner.ProvisionAsync(Identity(fixture, admin: true))).IsSuccess);
    }

    [Fact]
    public async Task DisabledProvisioning_ExcludesExistingExternalGrantsButPreservesManualRoles()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync(Mapping());
        var initial = await fixture.Provisioner.ProvisionAsync(Identity(fixture, admin: true));
        Assert.True(initial.IsSuccess);
        var user = await fixture.Users.FindByIdAsync(initial.Value.InternalUserId.ToString());
        Assert.NotNull(user);
        Assert.True((await fixture.Users.AddToRoleAsync(user, "User")).Succeeded);
        var disabled = new UserAuthorizationService(fixture.Db, fixture.Clock,
            Options.Create(new ExternalRoleMappingOptions { Enabled = false }));

        var authorization = await disabled.GetAsync(user.Id);

        Assert.NotNull(authorization);
        Assert.Equal(["User"], authorization.Roles);
    }

    [Fact]
    public async Task MissingAndDeletedUsers_HaveNoAuthorizationContext()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync(Mapping());
        Assert.Null(await fixture.Authorization.GetAsync(Guid.NewGuid()));
        var initial = await fixture.Provisioner.ProvisionAsync(Identity(fixture, admin: true));
        Assert.True(initial.IsSuccess);
        var user = await fixture.Users.FindByIdAsync(initial.Value.InternalUserId.ToString());
        Assert.NotNull(user);

        Assert.True((await fixture.Users.DeleteAsync(user)).Succeeded);

        Assert.Null(await fixture.Authorization.GetAsync(user.Id));
        Assert.Empty(await fixture.Db.Set<ExternalRoleGrant>().ToListAsync());
    }

    [Fact]
    public async Task InvalidRoleMapping_FailsWithoutGrantingUnknownPermissions()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync(new ExternalRoleMappingOptions { DefaultRole = "MissingRole" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Provisioner.ProvisionAsync(Identity(fixture)));

        Assert.Empty(await fixture.Db.Users.ToListAsync());
        Assert.Empty(await fixture.Db.Set<ExternalRoleGrant>().ToListAsync());
        Assert.Empty(await fixture.Db.UserRoles.ToListAsync());
    }

    [Fact]
    public async Task GrantReplacement_RollsBackWhenTheNewGrantCannotBeSaved()
    {
        var interceptor = new FailingGrantInsertInterceptor();
        await using var fixture = await IdentitySecurityFixture.CreateAsync(Mapping(), interceptor);
        var initial = await fixture.Provisioner.ProvisionAsync(Identity(fixture, admin: true));
        Assert.True(initial.IsSuccess);
        interceptor.FailNextInsert = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Provisioner.ProvisionAsync(Identity(fixture)));

        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(["Admin"], await fixture.AuthUsers.GetRolesAsync(initial.Value.InternalUserId));
        Assert.Single(await fixture.Db.Set<ExternalRoleGrant>().ToListAsync());
    }

    private static ExternalRoleMappingOptions Mapping() => new()
    {
        RoleGrantLifetimeMinutes = 5,
        Admin = new ExternalRoleMappingRuleOptions { Roles = ["external-admin"] }
    };

    private static ExternalIdentity Identity(IdentitySecurityFixture fixture, bool admin = false) => new()
    {
        Provider = "Oidc",
        Issuer = "https://identity.test",
        ProviderSubject = "role-test-user",
        Email = "user@example.test",
        EmailVerified = true,
        TokenExpiresAtUtc = fixture.Clock.GetUtcNow().AddMinutes(30),
        Roles = admin ? ["external-admin"] : []
    };

    private sealed class FailingGrantInsertInterceptor : DbCommandInterceptor
    {
        public bool FailNextInsert { get; set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return ValueTask.FromResult(result);
        }

        private void Check(DbCommand command)
        {
            if (FailNextInsert && command.CommandText.Contains("INSERT INTO \"ExternalRoleGrants\"", StringComparison.Ordinal))
            {
                FailNextInsert = false;
                throw new InvalidOperationException("Simulated persistence failure.");
            }
        }
    }
}
