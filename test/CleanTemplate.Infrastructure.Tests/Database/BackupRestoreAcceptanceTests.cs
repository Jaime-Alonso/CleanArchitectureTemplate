using CleanTemplate.Domain.Entities;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CleanTemplate.Infrastructure.Tests.Database;

public sealed class BackupRestoreAcceptanceTests
{
    [RecoveryFact]
    public async Task ExercisePhase()
    {
        var provider = Environment.GetEnvironmentVariable("RECOVERY_TEST_PROVIDER");
        var connection = Environment.GetEnvironmentVariable("RECOVERY_TEST_CONNECTION") ?? throw new InvalidOperationException("Missing recovery connection.");
        var phase = Environment.GetEnvironmentVariable("RECOVERY_TEST_PHASE");
        var database = phase == "verify" ? "recovery_target" : "recovery_source";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (provider == "SqlServer")
            options.UseSqlServer(new SqlConnectionStringBuilder(connection) { InitialCatalog = database, Pooling = false }.ConnectionString, x => x.MigrationsAssembly("CleanTemplate.Migrations.SqlServer"));
        else if (provider == "PostgreSql")
            options.UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { Database = database, Pooling = false }.ConnectionString, x => x.MigrationsAssembly("CleanTemplate.Migrations.PostgreSql"));
        else
            throw new InvalidOperationException("Invalid recovery provider.");

        await using var db = new ApplicationDbContext(options.Options);
        if (phase == "seed")
        {
            await db.Database.MigrateAsync();
            Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "recovery", NormalizedUserName = "RECOVERY", SecurityStamp = "stamp" };
            var role = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = "Admin", NormalizedName = "ADMIN" };
            var product = new Product("before", null, 12m, 4);
            product.Update("before", null, 13m, 5);
            var now = DateTime.UtcNow;
            var family = RefreshTokenFamily.Create(user.Id, now, now.AddDays(1), "stamp");
            db.AddRange(user, role, product, family);
            await db.SaveChangesAsync();
            db.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
            db.Add(new IdentityUserLogin<Guid> { UserId = user.Id, LoginProvider = "test", ProviderKey = "subject" });
            db.Add(new ExternalRoleGrant(user.Id, "test", "subject", role.Id, now.AddHours(1)));
            db.Add(RefreshToken.Create(user.Id, "jwt", new string('A', 64), now, now.AddHours(1), null, family.Id));
            await db.SaveChangesAsync();
        }
        else if (phase == "mutate")
        {
            var product = await db.Set<Product>().SingleAsync();
            product.Update("after", null, 20m, 6);
            db.RemoveRange(await db.UserRoles.ToListAsync());
            db.RemoveRange(await db.Set<ExternalRoleGrant>().ToListAsync());
            var token = await db.Set<RefreshToken>().SingleAsync();
            Assert.True(token.Revoke(DateTime.UtcNow, null));
            db.Add(new Product("after-backup", null, 1m, 1));
            await db.SaveChangesAsync();
        }
        else if (phase == "verify")
        {
            Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
            var product = await db.Set<Product>().SingleAsync();
            Assert.Equal("before", product.Name);
            Assert.Equal(2, product.Version);
            Assert.Equal(13m, product.Price);
            Assert.Equal(5, product.Stock);
            var user = Assert.Single(await db.Users.ToListAsync());
            var role = Assert.Single(await db.Roles.ToListAsync());
            var assignment = Assert.Single(await db.UserRoles.ToListAsync());
            var login = Assert.Single(await db.UserLogins.ToListAsync());
            var grant = Assert.Single(await db.Set<ExternalRoleGrant>().ToListAsync());
            Assert.Equal(user.Id, assignment.UserId);
            Assert.Equal(role.Id, assignment.RoleId);
            Assert.Equal(user.Id, login.UserId);
            Assert.Equal(user.Id, grant.UserId);
            Assert.Equal(role.Id, grant.RoleId);
            Assert.Equal(login.LoginProvider, grant.LoginProvider);
            Assert.Equal(login.ProviderKey, grant.ProviderKey);
            var token = await db.Set<RefreshToken>().SingleAsync();
            Assert.Equal(new string('A', 64), token.TokenHash);
            Assert.Null(token.RevokedAtUtc);
            var family = await db.Set<RefreshTokenFamily>().SingleAsync();
            Assert.Equal(user.Id, token.UserId);
            Assert.Equal(user.Id, family.UserId);
            Assert.Equal(family.Id, token.FamilyId);
            product.Update("restored-write", null, 14m, 7);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var written = await db.Set<Product>().SingleAsync();
            Assert.Equal("restored-write", written.Name);
            Assert.Equal(3, written.Version);
        }
        else throw new InvalidOperationException("Invalid recovery phase.");
    }

    private sealed class RecoveryFactAttribute : FactAttribute
    {
        public RecoveryFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RECOVERY_TEST_PROVIDER")))
                Skip = "Run scripts/tests/Test-BackupRestore.ps1 for isolated native recovery acceptance.";
        }
    }
}
