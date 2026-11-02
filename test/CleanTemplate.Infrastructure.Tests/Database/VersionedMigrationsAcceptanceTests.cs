using System.Text.RegularExpressions;
using CleanTemplate.Application.Contracts;
using CleanTemplate.Application.Products.ReadModels;
using CleanTemplate.Domain.Entities;
using CleanTemplate.Infrastructure.Database;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.Infrastructure.Repositories.Reads;
using CleanTemplate.Infrastructure.Repositories.Writes;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
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
    public async Task ProductReadsHandleExtremeOffsetsOnRealDatabase()
    {
        await using var owned = await Database.CreateAsync();
        await owned.Context.Database.MigrateAsync();
        var product = new Product("pagination", null, 1m, 1);
        owned.Context.Add(product);
        await owned.Context.SaveChangesAsync();
        var sqlServer = owned.Context.Database.IsSqlServer();
        var provider = sqlServer ? DatabaseProvider.SqlServer : DatabaseProvider.PostgreSql;
        ISqlDialect dialect = sqlServer ? new SqlServerDialect() : new PostgreSqlDialect();
        var reads = new ProductReadRepository(new ReadDbConnectionFactory(owned.Context.Database.GetConnectionString()!, provider), dialect);
        Assert.NotNull(await reads.GetByIdAsync(product.Id));
        var page = await reads.GetPagedAsync(new ProductSearchCriteria { Page = int.MaxValue, PageSize = int.MaxValue });
        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, page.TotalPages);
    }

    [AcceptanceFact]
    public async Task UnitOfWorkCommitsAndRollsBackMultipleFlushes()
    {
        await using var owned = await Database.CreateAsync();
        await owned.Context.Database.MigrateAsync();
        await using var db = owned.CreateContext();
        IProductWriteRepository first = new ProductWriteRepository(db);
        IProductWriteRepository second = new ProductWriteRepository(db);
        IUnitOfWork unit = new UnitOfWork(db);
        var a = new Product("committed-a", null, 1m, 1);
        var b = new Product("committed-b", null, 1m, 1);
        var committed = await unit.ExecuteInTransactionAsync(async ct =>
        {
            first.Add(a);
            Assert.True(await unit.SaveChangesAsync(ct));
            second.Add(b);
            Assert.True(await unit.SaveChangesAsync(ct));
            return Result.Success();
        });
        Assert.True(committed.IsSuccess);
        await using (var verify = owned.CreateContext())
            Assert.Equal(2, await verify.Set<Product>().CountAsync());

        foreach (var cancel in new[] { false, true })
        {
            await using var failingDb = owned.CreateContext();
            IProductWriteRepository one = new ProductWriteRepository(failingDb);
            IProductWriteRepository two = new ProductWriteRepository(failingDb);
            IUnitOfWork failing = new UnitOfWork(failingDb);
            using var cts = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<Exception>(() => failing.ExecuteInTransactionAsync(async ct =>
            {
                one.Add(new Product("rolled-back-a", null, 1m, 1));
                Assert.True(await failing.SaveChangesAsync(ct));
                two.Add(new Product("rolled-back-b", null, 1m, 1));
                if (cancel)
                {
                    cts.Cancel();
                    ct.ThrowIfCancellationRequested();
                }
                throw new InvalidOperationException("intermediate failure");
            }, cts.Token));
            Assert.Empty(failingDb.ChangeTracker.Entries());
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.SaveChangesAsync());
            await using var verify = owned.CreateContext();
            Assert.Equal(2, await verify.Set<Product>().CountAsync());
        }
    }

    [AcceptanceFact]
    public async Task BusinessFailureAfterFlushRollsBackAndInvalidatesScope()
    {
        await using var owned = await Database.CreateAsync();
        await owned.Context.Database.MigrateAsync();
        await using var db = owned.CreateContext();
        IUnitOfWork unit = new UnitOfWork(db);
        var error = Error.Validation("Product.BusinessRule", "Business rule failed.");
        var expected = Result.Failure(error);

        var result = await unit.ExecuteInTransactionAsync(async ct =>
        {
            db.Add(new Product("flushed", null, 1m, 1));
            Assert.True(await unit.SaveChangesAsync(ct));
            Assert.Equal(1, await db.Set<Product>().CountAsync(ct));
            db.Add(new Product("unflushed", null, 1m, 1));
            return expected;
        });

        Assert.Same(expected, result);
        Assert.Equal(error, result.FirstError);
        Assert.Empty(db.ChangeTracker.Entries());
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.SaveChangesAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteInTransactionAsync(_ => Task.FromResult(Result.Success())));
        await using var verify = owned.CreateContext();
        Assert.Equal(0, await verify.Set<Product>().CountAsync());
    }

    [AcceptanceFact]
    public async Task TransactionDoesNotFlushPendingChangesOnSuccess()
    {
        await using var owned = await Database.CreateAsync();
        await owned.Context.Database.MigrateAsync();
        await using var db = owned.CreateContext();
        IUnitOfWork unit = new UnitOfWork(db);
        var result = await unit.ExecuteInTransactionAsync(ct =>
        {
            db.Add(new Product("unflushed", null, 1m, 1));
            return Task.FromResult(Result.Success());
        });
        Assert.True(result.IsSuccess);
        await using var verify = owned.CreateContext();
        Assert.Equal(0, await verify.Set<Product>().CountAsync());
    }

    [AcceptanceFact]
    public async Task ConcurrencyConflictAfterFlushAbortsEvenWhenCallbackIgnoresConflict()
    {
        await using var owned = await Database.CreateAsync();
        await owned.Context.Database.MigrateAsync();
        var product = new Product("original", null, 1m, 1);
        owned.Context.Add(product);
        await owned.Context.SaveChangesAsync();
        await using var staleDb = owned.CreateContext();
        await using var winnerDb = owned.CreateContext();
        IProductWriteRepository staleRepository = new ProductWriteRepository(staleDb);
        var stale = (await staleRepository.GetByIdAsync(product.Id))!;
        var winner = (await new ProductWriteRepository(winnerDb).GetByIdAsync(product.Id))!;
        winner.Update("winner", null, 2m, 2);
        Assert.True(await new UnitOfWork(winnerDb).SaveChangesAsync());

        IUnitOfWork unit = new UnitOfWork(staleDb);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteInTransactionAsync(async ct =>
        {
            new ProductWriteRepository(staleDb).Add(new Product("rolled back", null, 1m, 1));
            Assert.True(await unit.SaveChangesAsync(ct));
            stale.Update("stale", null, 3m, 3);
            Assert.False(await unit.SaveChangesAsync(ct));
            return Result.Success();
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.SaveChangesAsync());
        await using var verify = owned.CreateContext();
        Assert.Equal("winner", (await verify.Set<Product>().SingleAsync()).Name);
    }

    [AcceptanceFact]
    public async Task ProductVersionUpgradePreservesExistingProductAndRejectsStaleWrites()
    {
        await using var owned = await Database.CreateAsync();
        var db = owned.Context;
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToArray();
        var previous = migrations.Single(x => x.EndsWith("_AddRefreshTokenFamilies", StringComparison.Ordinal));
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
        Assert.True(await new UnitOfWork(db).SaveChangesAsync());
        Assert.False(await new UnitOfWork(other).SaveChangesAsync());
        other.ChangeTracker.Clear();
        var saved = (await second.GetByIdAsync(id))!;
        Assert.Equal("winner", saved.Name);
        Assert.Equal(2, saved.Version);
        db.ChangeTracker.Clear();
        var stale = (await first.GetByIdAsync(id))!;
        saved.Update("newer", null, 15m, 7);
        Assert.True(await new UnitOfWork(other).SaveChangesAsync());
        first.Remove(stale);
        Assert.False(await new UnitOfWork(db).SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal("newer", (await first.GetByIdAsync(id))!.Name);
    }

    [AcceptanceFact]
    public async Task ProductIntegrityUpgradeFailsForInvalidLegacyRowsAndCanBeRetried()
    {
        await using var owned = await Database.CreateAsync();
        var db = owned.Context;
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToArray();
        var target = migrations.Single(x => x.EndsWith("_EnforceProductIntegrity", StringComparison.Ordinal));
        var targetIndex = Array.IndexOf(migrations, target);
        var previous = migrations[targetIndex - 1];
        await owned.ExecuteScriptAsync(migrator.GenerateScript(toMigration: previous));
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Products\" (\"Id\", \"Name\", \"Price\", \"Stock\") VALUES ({id}, {"legacy"}, {-1m}, {1})");
        var upgrade = migrator.GenerateScript(fromMigration: previous, toMigration: target, options: MigrationsSqlGenerationOptions.Idempotent);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => owned.ExecuteScriptAsync(upgrade));
        Assert.Contains("Invalid legacy products", error.Message);
        Assert.Equal(migrations[..targetIndex], await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(-1m, await db.Database.SqlQuery<decimal>($"SELECT \"Price\" AS \"Value\" FROM \"Products\" WHERE \"Id\" = {id}").SingleAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Products\" SET \"Price\" = {1m} WHERE \"Id\" = {id}");
        await owned.ExecuteScriptAsync(upgrade);
        Assert.Equal(migrations[..(targetIndex + 1)], await db.Database.GetAppliedMigrationsAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Products\" SET \"Stock\" = {-1} WHERE \"Id\" = {id}"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Products\" SET \"Price\" = {-1m} WHERE \"Id\" = {id}"));
    }

    [AcceptanceFact]
    public async Task ProductIntegrityEnforcesUtf16AndRejectsInvalidLegacyData()
    {
        await using var owned = await Database.CreateAsync();
        var db = owned.Context;
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToArray();
        var target = migrations.Single(x => x.EndsWith("_EnforceProductIntegrity", StringComparison.Ordinal));
        var targetIndex = Array.IndexOf(migrations, target);
        var previous = migrations[targetIndex - 1];
        await owned.ExecuteScriptAsync(migrator.GenerateScript(toMigration: previous));
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Products\" (\"Id\", \"Name\", \"Description\", \"Price\", \"Stock\") VALUES ({id}, {"legacy"}, {"ok"}, {1m}, {1})");
        var upgrade = migrator.GenerateScript(fromMigration: previous, toMigration: target, options: MigrationsSqlGenerationOptions.Idempotent);
        if (db.Database.IsNpgsql())
        {
            foreach (var column in new[] { "Name", "Description", "Price" })
            {
                var invalid = column switch { "Name" => string.Concat(Enumerable.Repeat("😀", 101)), "Description" => string.Concat(Enumerable.Repeat("😀", 501)), _ => "NaN" };
                if (column == "Price")
                    await db.Database.ExecuteSqlRawAsync("UPDATE \"Products\" SET \"Price\" = 'NaN'");
                else if (column == "Name")
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Products\" SET \"Name\" = {invalid}");
                else
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Products\" SET \"Description\" = {invalid}");
                var error = await Assert.ThrowsAnyAsync<Exception>(() => owned.ExecuteScriptAsync(upgrade));
                Assert.Contains("Invalid legacy products", error.Message);
                Assert.Equal(migrations[..targetIndex], await db.Database.GetAppliedMigrationsAsync());
                Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE conrelid = '\"Products\"'::regclass AND conname LIKE 'CK_Products_%'").SingleAsync());
                if (column == "Price")
                    Assert.True(await db.Database.SqlQueryRaw<bool>("SELECT (\"Price\" = 'NaN'::numeric) AS \"Value\" FROM \"Products\"").SingleAsync());
                else if (column == "Name")
                    Assert.Equal(invalid, await db.Database.SqlQueryRaw<string>("SELECT \"Name\" AS \"Value\" FROM \"Products\"").SingleAsync());
                else
                    Assert.Equal(invalid, await db.Database.SqlQueryRaw<string>("SELECT \"Description\" AS \"Value\" FROM \"Products\"").SingleAsync());
                await db.Database.ExecuteSqlRawAsync("UPDATE \"Products\" SET \"Name\" = 'legacy', \"Description\" = 'ok', \"Price\" = 1");
            }
        }
        await owned.ExecuteScriptAsync(upgrade);
        var validName = string.Concat(Enumerable.Repeat("😀", 100));
        var validDescription = string.Concat(Enumerable.Repeat("😀", 500));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Products\" SET \"Name\" = {validName}, \"Description\" = {validDescription}, \"Price\" = {9999999999999999.99m}");
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Products\" SET \"Name\" = {validName + "x"}"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Products\" SET \"Description\" = {validDescription + "x"}"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Products\" SET \"Price\" = {9999999999999999.99m + 0.01m}"));
        if (db.Database.IsNpgsql())
            await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"Products\" SET \"Price\" = 'NaN'"));
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
