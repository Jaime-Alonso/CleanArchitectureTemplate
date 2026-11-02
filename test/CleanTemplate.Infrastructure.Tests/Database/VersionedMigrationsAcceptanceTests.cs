using System.Text.RegularExpressions;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.Infrastructure.Repositories.Writes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CleanTemplate.Infrastructure.Tests.Database;

public sealed class VersionedMigrationsAcceptanceTests
{
    [AcceptanceFact]
    public async Task ProductVersionUpgradePreservesExistingProductAndRejectsStaleWrites()
    {
        await using var owned = await Database.CreateAsync();
        var db = owned.Context;
        var migrator = db.GetService<IMigrator>();
        var previous = db.Database.GetMigrations().Reverse().Skip(1).First();
        await owned.ExecuteScriptAsync(migrator.GenerateScript(toMigration: previous));
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Products\" (\"Id\", \"Name\", \"Price\", \"Stock\") VALUES ({id}, {"legacy"}, {12m}, {4})");
        await owned.ExecuteScriptAsync(migrator.GenerateScript(fromMigration: previous, options: MigrationsSqlGenerationOptions.Idempotent));
        await using var other = owned.CreateContext();
        var first = new ProductWriteRepository(db);
        var second = new ProductWriteRepository(other);
        var a = (await first.GetByIdAsync(id))!;
        var b = (await second.GetByIdAsync(id))!;
        Assert.Equal(1, a.Version);
        Assert.Equal("legacy", a.Name);
        a.Update("winner", null, 13m, 5);
        b.Update("loser", null, 14m, 6);
        Assert.True(await first.TrySaveChangesAsync());
        Assert.False(await second.TrySaveChangesAsync());
        other.ChangeTracker.Clear();
        var saved = (await second.GetByIdAsync(id))!;
        Assert.Equal("winner", saved.Name);
        Assert.Equal(2, saved.Version);
        db.ChangeTracker.Clear();
        var stale = (await first.GetByIdAsync(id))!;
        saved.Update("newer", null, 15m, 7);
        Assert.True(await second.TrySaveChangesAsync());
        first.Remove(stale);
        Assert.False(await first.TrySaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal("newer", (await first.GetByIdAsync(id))!.Name);
    }

    [AcceptanceFact]
    public async Task ScriptCreatesAndReappliesWithoutChangingData()
    {
        await using var owned = await Database.CreateAsync();
        var db = owned.Context;
        var script = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        await owned.ExecuteScriptAsync(script);
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Products\" (\"Id\", \"Name\", \"Description\", \"Price\", \"Stock\") VALUES ({id}, {"product"}, {"description"}, {12.50m}, {4})");
        var history = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(db.Database.GetMigrations(), history);
        await owned.ExecuteScriptAsync(script);
        Assert.Equal(history, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM \"Products\" WHERE \"Id\" = {id} AND \"Stock\" = {4}").SingleAsync());
    }

    [AcceptanceFact]
    public async Task UpgradeFromInitialPreservesDataAndIsIdempotent()
    {
        await using var owned = await Database.CreateAsync();
        var db = owned.Context;
        var migrator = db.GetService<IMigrator>();
        var initial = db.Database.GetMigrations().First();
        await owned.ExecuteScriptAsync(migrator.GenerateScript(toMigration: initial));
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "legacy", NormalizedUserName = "LEGACY", Email = "legacy@example.test", NormalizedEmail = "LEGACY@EXAMPLE.TEST", PasswordHash = "hash", SecurityStamp = "stamp" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var upgrade = migrator.GenerateScript(fromMigration: initial, options: MigrationsSqlGenerationOptions.Idempotent);
        await owned.ExecuteScriptAsync(upgrade);
        await owned.ExecuteScriptAsync(upgrade);
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
        db.ChangeTracker.Clear();
        var saved = await db.Users.SingleAsync();
        Assert.Equal(user.Id, saved.Id);
        Assert.Equal("hash", saved.PasswordHash);
        Assert.Equal("stamp", saved.SecurityStamp);
    }

    [AcceptanceFact]
    public async Task DuplicateEmailsFailThenRecoverWithoutForgingHistory()
    {
        await using var owned = await Database.CreateAsync();
        var db = owned.Context;
        var migrator = db.GetService<IMigrator>();
        var initial = db.Database.GetMigrations().First();
        await owned.ExecuteScriptAsync(migrator.GenerateScript(toMigration: initial));
        db.Users.AddRange(
            new ApplicationUser { Id = Guid.NewGuid(), UserName = "one", NormalizedUserName = "ONE", NormalizedEmail = "DUPLICATE" },
            new ApplicationUser { Id = Guid.NewGuid(), UserName = "two", NormalizedUserName = "TWO", NormalizedEmail = "DUPLICATE" });
        await db.SaveChangesAsync();
        var upgrade = migrator.GenerateScript(fromMigration: initial, options: MigrationsSqlGenerationOptions.Idempotent);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => owned.ExecuteScriptAsync(upgrade));
        if (error is SqlException sqlError)
            Assert.Contains(sqlError.Number, new[] { 1505, 2601, 2627 });
        else
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error).SqlState);
        Assert.Contains("EmailIndex", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { initial }, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(2, await db.Users.CountAsync(u => u.NormalizedEmail == "DUPLICATE"));
        await db.Users.Where(u => u.NormalizedUserName == "TWO").ExecuteUpdateAsync(s => s.SetProperty(u => u.NormalizedEmail, "OTHER"));
        await owned.ExecuteScriptAsync(upgrade);
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(2, await db.Users.CountAsync());
        var duplicate = new ApplicationUser { Id = Guid.NewGuid(), UserName = "three", NormalizedUserName = "THREE", NormalizedEmail = "DUPLICATE" };
        db.Users.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [AcceptanceFact]
    public async Task EnsureCreatedSideBySideCopiesRepresentativeProductsAndIdentity()
    {
        await using var source = await Database.CreateAsync(createEmpty: false);
        await source.Context.Database.EnsureCreatedAsync();
        await using var target = await Database.CreateAsync();
        await target.ExecuteScriptAsync(target.Context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent));
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        source.Context.Users.Add(new ApplicationUser { Id = userId, UserName = "copy", NormalizedUserName = "COPY", Email = "copy@example.test", NormalizedEmail = "COPY@EXAMPLE.TEST", PasswordHash = "password-hash", SecurityStamp = "security-stamp" });
        await source.Context.SaveChangesAsync();
        await source.Context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Products\" (\"Id\", \"Name\", \"Description\", \"Price\", \"Stock\") VALUES ({productId}, {"sample"}, {"details"}, {42.25m}, {7})");
        var user = await source.Context.Users.AsNoTracking().SingleAsync();
        await target.Context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"AspNetUsers\" (\"Id\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"PasswordHash\", \"SecurityStamp\", \"EmailConfirmed\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnabled\", \"AccessFailedCount\") VALUES ({user.Id}, {user.UserName}, {user.NormalizedUserName}, {user.Email}, {user.NormalizedEmail}, {user.PasswordHash}, {user.SecurityStamp}, {user.EmailConfirmed}, {user.PhoneNumberConfirmed}, {user.TwoFactorEnabled}, {user.LockoutEnabled}, {user.AccessFailedCount})");
        // Explicit columns and parameterized values, not cross-database SQL or history fabrication.
        var product = await source.Context.Database.SqlQuery<ProductRow>($"SELECT \"Id\", \"Name\", \"Description\", \"Price\", \"Stock\" FROM \"Products\"").SingleAsync();
        await target.Context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Products\" (\"Id\", \"Name\", \"Description\", \"Price\", \"Stock\") VALUES ({product.Id}, {product.Name}, {product.Description}, {product.Price}, {product.Stock})");
        var copied = await target.Context.Users.AsNoTracking().SingleAsync();
        Assert.Equal(user.Id, copied.Id);
        Assert.Equal(user.PasswordHash, copied.PasswordHash);
        Assert.Equal(user.SecurityStamp, copied.SecurityStamp);
        Assert.Equal(user.NormalizedEmail, copied.NormalizedEmail);
        var copiedProduct = await target.Context.Database.SqlQuery<ProductRow>($"SELECT \"Id\", \"Name\", \"Description\", \"Price\", \"Stock\" FROM \"Products\"").SingleAsync();
        Assert.Equal(product, copiedProduct);
        Assert.Equal(target.Context.Database.GetMigrations(), await target.Context.Database.GetAppliedMigrationsAsync());
    }

    private sealed record ProductRow(Guid Id, string Name, string? Description, decimal Price, int Stock);

    private sealed class AcceptanceFactAttribute : FactAttribute
    {
        public AcceptanceFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VERSIONED_MIGRATIONS_TEST_PROVIDER")))
                Skip = "Run scripts/tests/Test-VersionedMigrations.ps1 for isolated real-engine acceptance.";
        }
    }

    private sealed class Database(ApplicationDbContext context, bool sqlServer) : IAsyncDisposable
    {
        public ApplicationDbContext Context => context;

        public ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>();
            if (sqlServer)
                options.UseSqlServer(context.Database.GetConnectionString(), x => x.MigrationsAssembly("CleanTemplate.Migrations.SqlServer"));
            else
                options.UseNpgsql(context.Database.GetConnectionString(), x => x.MigrationsAssembly("CleanTemplate.Migrations.PostgreSql"));
            return new ApplicationDbContext(options.Options);
        }

        public static async Task<Database> CreateAsync(bool createEmpty = true)
        {
            var provider = Environment.GetEnvironmentVariable("VERSIONED_MIGRATIONS_TEST_PROVIDER");
            var connection = Environment.GetEnvironmentVariable("VERSIONED_MIGRATIONS_TEST_CONNECTION") ?? throw new InvalidOperationException("Missing acceptance connection.");
            var name = $"versioned_migrations_{Guid.NewGuid():N}";
            var options = new DbContextOptionsBuilder<ApplicationDbContext>();
            var sqlServer = provider == "SqlServer";
            if (sqlServer)
                options.UseSqlServer(new SqlConnectionStringBuilder(connection) { InitialCatalog = name, Pooling = false }.ConnectionString, x => x.MigrationsAssembly("CleanTemplate.Migrations.SqlServer"));
            else if (provider == "PostgreSql")
                options.UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { Database = name, Pooling = false }.ConnectionString, x => x.MigrationsAssembly("CleanTemplate.Migrations.PostgreSql"));
            else
                throw new InvalidOperationException("Unsupported acceptance provider.");
            var result = new Database(new ApplicationDbContext(options.Options), sqlServer);
            try
            {
                if (createEmpty)
                    await result.Context.GetService<IRelationalDatabaseCreator>().CreateAsync();
                return result;
            }
            catch
            {
                await result.DisposeAsync();
                throw;
            }
        }

        public async Task ExecuteScriptAsync(string script)
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();
            try
            {
                var batches = sqlServer ? Regex.Split(script, @"(?im)^\s*GO\s*(?:--[^\r\n]*)?\r?$") : [script];
                foreach (var batch in batches.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = batch;
                    command.CommandTimeout = 120;
                    await command.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                await connection.CloseAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { await context.Database.EnsureDeletedAsync(); }
            finally { await context.DisposeAsync(); }
        }
    }
}
