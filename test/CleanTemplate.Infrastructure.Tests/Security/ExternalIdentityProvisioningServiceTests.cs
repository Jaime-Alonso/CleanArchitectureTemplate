using System.Diagnostics.Metrics;
using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Tests.Testing;
using CleanTemplate.SharedKernel.Errors;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CleanTemplate.Infrastructure.Tests.Security;

[Collection("Privilege audit")]
public sealed class ExternalIdentityProvisioningServiceTests
{
    [Fact]
    public async Task LockedDatabase_RecordsContentionAndReturnsConflict()
    {
        var records = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == "CleanTemplate.Security" && instrument.Name == "security.provisioning.contention") l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) => records.Add((value, tags.ToArray())));
        listener.Start();
        var connectionString = $"Data Source=provision-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=1";
        await using var fixture = await IdentitySecurityFixture.CreateAsync(sharedConnectionString: connectionString);
        await using var blocker = new SqliteConnection(connectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        try
        {
            await using var command = blocker.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "UPDATE AspNetRoles SET Name = Name";
            await command.ExecuteNonQueryAsync();
            var result = await fixture.Provisioner.ProvisionAsync(Identity());
            Assert.True(result.IsFailure);
            Assert.Equal(ErrorType.Conflict, result.FirstError?.Type);
            Assert.Equal("Auth.ExternalProvisioningConflict", result.FirstError?.Code);
            Assert.Equal(3, records.Count);
            Assert.All(records, record => { Assert.True(record.Value > 0); Assert.Empty(record.Tags); });
        }
        finally { await transaction.RollbackAsync(); }
    }

    [Fact]
    public async Task RejectedProvisioning_RecordsLowCardinalityMetric()
    {
        var records = new List<(string Name, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == "CleanTemplate.Security") l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => records.Add((instrument.Name, tags.ToArray())));
        listener.Start();
        await using var fixture = await IdentitySecurityFixture.CreateAsync();
        var result = await fixture.Provisioner.ProvisionAsync(Identity() with { EmailVerified = false });
        Assert.True(result.IsFailure);
        Assert.Empty(Assert.Single(records, r => r.Name == "security.provisioning.rejected").Tags);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchingLocalEmail_NeverLinksOrChangesExistingAccount(bool verified)
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync();
        var local = new ApplicationUser { Id = Guid.NewGuid(), UserName = "local-admin", Email = "admin@example.test" };
        Assert.True((await fixture.Users.CreateAsync(local)).Succeeded);
        Assert.True((await fixture.Users.AddToRoleAsync(local, "Admin")).Succeeded);

        var result = await fixture.Provisioner.ProvisionAsync(Identity() with { Email = local.Email, EmailVerified = verified });

        Assert.True(result.IsFailure);
        Assert.Equal(verified ? "Auth.ExternalAccountLinkRequired" : "Auth.ExternalEmailNotVerified", result.FirstError?.Code);
        Assert.Empty(await fixture.Users.GetLoginsAsync(local));
        Assert.Equal(["Admin"], await fixture.Users.GetRolesAsync(local));
        Assert.Equal(1, await fixture.Db.Users.CountAsync());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("user@example.test", false)]
    [InlineData(null, true)]
    [InlineData("", true)]
    public async Task MissingOrUnverifiedEmail_DoesNotCreateAnAccount(string? email, bool verified)
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync();

        var result = await fixture.Provisioner.ProvisionAsync(Identity() with { Email = email, EmailVerified = verified });

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.ExternalEmailNotVerified", result.FirstError?.Code);
        Assert.Empty(await fixture.Db.Users.ToListAsync());
    }

    [Fact]
    public async Task SameSubjectFromDifferentIssuers_CreatesSeparateAccounts()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync();
        var first = await fixture.Provisioner.ProvisionAsync(Identity());
        var second = await fixture.Provisioner.ProvisionAsync(Identity() with { Issuer = "https://other-issuer.test", Email = "other@example.test" });

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value.InternalUserId, second.Value.InternalUserId);
        Assert.Equal(2, await fixture.Db.UserLogins.Select(login => login.LoginProvider).Distinct().CountAsync());
    }

    [Fact]
    public async Task AnotherIssuerWithMatchingEmail_CannotReuseTheFirstIssuersAccount()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync();
        Assert.True((await fixture.Provisioner.ProvisionAsync(Identity())).IsSuccess);

        var result = await fixture.Provisioner.ProvisionAsync(Identity() with { Issuer = "https://other-issuer.test" });

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.ExternalAccountLinkRequired", result.FirstError?.Code);
        Assert.Equal(1, await fixture.Db.Users.CountAsync());
        Assert.Equal(1, await fixture.Db.UserLogins.CountAsync());
    }

    [Fact]
    public async Task IssuerAndSubject_AreCaseSensitive()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync();
        var first = await fixture.Provisioner.ProvisionAsync(Identity());
        var issuerChanged = await fixture.Provisioner.ProvisionAsync(Identity() with { Issuer = "https://ISSUER.test", Email = "issuer@example.test" });
        var subjectChanged = await fixture.Provisioner.ProvisionAsync(Identity() with { ProviderSubject = "SUBJECT", Email = "subject@example.test" });

        Assert.True(first.IsSuccess);
        Assert.True(issuerChanged.IsSuccess);
        Assert.True(subjectChanged.IsSuccess);
        Assert.Equal(3, new[] { first.Value.InternalUserId, issuerChanged.Value.InternalUserId, subjectChanged.Value.InternalUserId }.Distinct().Count());
    }

    [Fact]
    public async Task LongIssuerAndSubject_ProduceBoundedLoginKeysAndUsername()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync();

        var result = await fixture.Provisioner.ProvisionAsync(Identity() with
        {
            Issuer = $"https://issuer.test/{new string('a', 512)}",
            ProviderSubject = new string('b', 512)
        });

        Assert.True(result.IsSuccess);
        var login = Assert.Single(await fixture.Db.UserLogins.ToListAsync());
        Assert.StartsWith("oidc:", login.LoginProvider);
        Assert.Equal(69, login.LoginProvider.Length);
        Assert.Equal(64, login.ProviderKey.Length);
        var user = await fixture.Users.FindByIdAsync(result.Value.InternalUserId.ToString());
        Assert.NotNull(user);
        Assert.Equal(73, user.UserName?.Length);
    }

    [Fact]
    public async Task DisplayProviderRenameAndVerifiedProfileChange_KeepStableIdentity()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync();
        var first = await fixture.Provisioner.ProvisionAsync(Identity());
        Assert.True(first.IsSuccess);
        var user = await fixture.Users.FindByIdAsync(first.Value.InternalUserId.ToString());
        Assert.NotNull(user);
        var username = user.UserName;

        var second = await fixture.Provisioner.ProvisionAsync(Identity() with
        {
            Provider = "Renamed provider",
            Email = "changed@example.test",
            Name = "Updated profile"
        });

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.InternalUserId, second.Value.InternalUserId);
        await fixture.Db.Entry(user).ReloadAsync(); // Provisioning now uses an isolated context per transaction.
        Assert.Equal(username, user.UserName);
        Assert.Equal("changed@example.test", user.Email);
        Assert.True(user.EmailConfirmed);
        Assert.Single(await fixture.Users.GetLoginsAsync(user));
    }

    [Fact]
    public async Task ChangedTenant_IsRejectedWithoutChangingTheAccount()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync();
        var first = await fixture.Provisioner.ProvisionAsync(Identity());
        Assert.True(first.IsSuccess);

        var result = await fixture.Provisioner.ProvisionAsync(Identity() with { TenantId = "another-tenant", Name = "Changed" });

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.ExternalTenantChanged", result.FirstError?.Code);
        var user = await fixture.Users.FindByIdAsync(first.Value.InternalUserId.ToString());
        Assert.NotNull(user);
        Assert.Equal("tenant-1", user.TenantId);
        Assert.Equal("Original profile", user.DisplayName);
    }

    [Fact]
    public async Task ChangedEmailOwnedByAnotherAccount_IsRejected()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync();
        var first = await fixture.Provisioner.ProvisionAsync(Identity());
        Assert.True(first.IsSuccess);
        var other = new ApplicationUser { Id = Guid.NewGuid(), UserName = "other-user", Email = "other@example.test" };
        Assert.True((await fixture.Users.CreateAsync(other)).Succeeded);

        var result = await fixture.Provisioner.ProvisionAsync(Identity() with { Email = other.Email });

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.ExternalAccountLinkRequired", result.FirstError?.Code);
        var user = await fixture.Users.FindByIdAsync(first.Value.InternalUserId.ToString());
        Assert.NotNull(user);
        Assert.Equal("user@example.test", user.Email);
        Assert.Empty(await fixture.Users.GetLoginsAsync(other));
    }

    [Fact]
    public async Task LegacyProviderSubjectLogin_IsNotSilentlyMigrated()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync();
        var legacy = new ApplicationUser { Id = Guid.NewGuid(), UserName = "legacy-user", Email = "user@example.test" };
        Assert.True((await fixture.Users.CreateAsync(legacy)).Succeeded);
        Assert.True((await fixture.Users.AddLoginAsync(legacy, new UserLoginInfo("Oidc", "subject", "Oidc"))).Succeeded);

        var result = await fixture.Provisioner.ProvisionAsync(Identity());

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.ExternalAccountLinkRequired", result.FirstError?.Code);
        var login = Assert.Single(await fixture.Users.GetLoginsAsync(legacy));
        Assert.Equal("Oidc", login.LoginProvider);
        Assert.Equal("subject", login.ProviderKey);
    }

    [Fact]
    public async Task UnverifiedProfileAfterSuccessfulProvisioning_DoesNotReuseAPreviousDecision()
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync();
        Assert.True((await fixture.Provisioner.ProvisionAsync(Identity())).IsSuccess);

        var rejected = await fixture.Provisioner.ProvisionAsync(Identity() with { EmailVerified = false });

        Assert.True(rejected.IsFailure);
        Assert.Equal("Auth.ExternalEmailNotVerified", rejected.FirstError?.Code);
    }

    [Theory]
    [InlineData("", "subject")]
    [InlineData("https://issuer.test", "")]
    public async Task MissingIssuerOrSubject_IsRejected(string issuer, string subject)
    {
        await using var fixture = await IdentitySecurityFixture.CreateAsync();

        var result = await fixture.Provisioner.ProvisionAsync(Identity() with { Issuer = issuer, ProviderSubject = subject });

        Assert.True(result.IsFailure);
        Assert.Empty(await fixture.Db.Users.ToListAsync());
    }

    private static ExternalIdentity Identity() => new()
    {
        Provider = "Oidc",
        Issuer = "https://issuer.test",
        TokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
        ProviderSubject = "subject",
        Email = "user@example.test",
        EmailVerified = true,
        Name = "Original profile",
        TenantId = "tenant-1"
    };

}
