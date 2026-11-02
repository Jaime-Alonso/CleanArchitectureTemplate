using CleanTemplate.Domain.Entities;
using CleanTemplate.Infrastructure.Extensions;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.Infrastructure.Repositories.Writes;
using CleanTemplate.Migrations.PostgreSql;
using CleanTemplate.Migrations.SqlServer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CleanTemplate.Infrastructure.Tests.Database;

public sealed class VersionedMigrationsTests
{
    [Fact]
    public async Task SqliteIndependentContextsRejectStaleUpdateAndDelete()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var firstContext = new ApplicationDbContext(options);
        await using var secondContext = new ApplicationDbContext(options);
        await firstContext.Database.EnsureCreatedAsync();
        var product = new Product("original", null, 1m, 1);
        firstContext.Add(product);
        await firstContext.SaveChangesAsync();
        firstContext.ChangeTracker.Clear();
        var first = new ProductWriteRepository(firstContext);
        var second = new ProductWriteRepository(secondContext);
        var a = (await first.GetByIdAsync(product.Id))!;
        var b = (await second.GetByIdAsync(product.Id))!;
        a.Update("winner", null, 2m, 2);
        b.Update("loser", null, 3m, 3);
        Assert.True(await first.TrySaveChangesAsync());
        Assert.False(await second.TrySaveChangesAsync());
        secondContext.ChangeTracker.Clear();
        var current = (await second.GetByIdAsync(product.Id))!;
        Assert.Equal("winner", current.Name);
        Assert.Equal(2, current.Version);
        firstContext.ChangeTracker.Clear();
        var stale = (await first.GetByIdAsync(product.Id))!;
        current.Update("newer", null, 4m, 4);
        Assert.True(await second.TrySaveChangesAsync());
        first.Remove(stale);
        Assert.False(await first.TrySaveChangesAsync());
        firstContext.ChangeTracker.Clear();
        Assert.Equal("newer", (await first.GetByIdAsync(product.Id))!.Name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Provider_has_own_initial_migration_and_current_snapshot(bool sqlServer)
    {
        using var context = CreateContext(sqlServer);

        var migrations = context.Database.GetMigrations().ToArray();
        Assert.NotEmpty(migrations);
        Assert.EndsWith("_InitialCreate", migrations[0]);
        Assert.Single(migrations, migration => migration.EndsWith("_EnforceUniqueIdentityEmail", StringComparison.Ordinal));
        Assert.Single(migrations, migration => migration.EndsWith("_AddRefreshTokenFamilies", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());

        var assembly = context.GetService<IMigrationsAssembly>();
        Assert.Equal(sqlServer ? "CleanTemplate.Migrations.SqlServer" : "CleanTemplate.Migrations.PostgreSql",
            assembly.Assembly.GetName().Name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Provider_generates_create_idempotent_upgrade_and_rollback_scripts(bool sqlServer)
    {
        using var context = CreateContext(sqlServer);
        var migrator = context.GetService<IMigrator>();
        var migrations = context.Database.GetMigrations().ToArray();
        var migration = migrations[^1];

        var create = migrator.GenerateScript();
        var upgrade = migrator.GenerateScript("0", migration, MigrationsSqlGenerationOptions.Idempotent);
        var rollback = migrator.GenerateScript(migration, "0");

        foreach (var table in new[] { "Products", "AspNetUsers", "AspNetRoles", "RefreshTokens", "ExternalRoleGrants", "RefreshTokenFamilies" })
        {
            Assert.Contains(table, create);
            Assert.Contains(table, rollback);
        }

        Assert.Contains("__EFMigrationsHistory", upgrade);
        Assert.Contains(migration, upgrade);
        Assert.Contains("IF NOT EXISTS", upgrade);
        Assert.Contains(sqlServer ? "uniqueidentifier" : "uuid", create);
        Assert.Contains("DROP TABLE", rollback);

        var emailMigration = Assert.Single(migrations, name => name.EndsWith("_EnforceUniqueIdentityEmail", StringComparison.Ordinal));
        var precedingEmailMigration = migrations[Array.IndexOf(migrations, emailMigration) - 1];
        var emailUpgrade = migrator.GenerateScript(precedingEmailMigration, emailMigration);
        Assert.Contains("EmailIndex", emailUpgrade);
        Assert.Contains("UNIQUE", emailUpgrade);
        Assert.DoesNotContain("DROP TABLE", emailUpgrade);
        if (sqlServer)
        {
            Assert.Contains("[NormalizedEmail] IS NOT NULL", emailUpgrade);
        }
    }

    [Fact]
    public async Task Ephemeral_database_can_be_initialized_repeatedly_in_development()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);

        await context.InitializeDatabaseAsync(allowEphemeralDatabase: true);
        await context.InitializeDatabaseAsync(allowEphemeralDatabase: true);

        Assert.True(await context.Database.CanConnectAsync());
        Assert.Equal(0, await context.Users.CountAsync());
    }

    [Theory]
    [InlineData("Data Source=:memory:", false)]
    [InlineData("Data Source=must-not-be-created.db", true)]
    public async Task Schema_creation_rejects_production_or_persistent_sqlite(string connectionString, bool allowEphemeral)
    {
        await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString).Options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.InitializeDatabaseAsync(allowEphemeral));

        Assert.Contains("only supported for in-memory", exception.Message);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Persistent_provider_without_migrations_fails_without_creating_schema(bool sqlServer)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (sqlServer)
        {
            options.UseSqlServer("Server=localhost;Database=must-not-be-created;Integrated Security=true");
        }
        else
        {
            options.UseNpgsql("Host=localhost;Database=must-not-be-created;Username=postgres");
        }

        await using var context = new ApplicationDbContext(options.Options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.InitializeDatabaseAsync(allowEphemeralDatabase: true));

        Assert.Contains("No versioned migrations", exception.Message);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private static ApplicationDbContext CreateContext(bool sqlServer) => sqlServer
        ? new SqlServerDesignTimeDbContextFactory().CreateDbContext([])
        : new PostgreSqlDesignTimeDbContextFactory().CreateDbContext([]);
}
