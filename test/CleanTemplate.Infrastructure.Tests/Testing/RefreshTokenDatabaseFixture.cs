using CleanTemplate.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CleanTemplate.Infrastructure.Tests.Testing;

/// <summary>Independent DI containers and connections; production providers use real migrations in a uniquely owned database.</summary>
internal sealed class RefreshTokenDatabaseFixture(
    ServiceProvider first, ServiceProvider second, string? sqlitePath) : IAsyncDisposable
{
    public ServiceProvider First => first;
    public ServiceProvider Second => second;

    public static Task<RefreshTokenDatabaseFixture> CreateAsync(params IInterceptor[] interceptors) => CreateCoreAsync(false, interceptors);

    public static Task<RefreshTokenDatabaseFixture> CreateLegacySchemaAsync() => CreateCoreAsync(true, []);

    private static async Task<RefreshTokenDatabaseFixture> CreateCoreAsync(bool legacySchema, IInterceptor[] interceptors)
    {
        var provider = Environment.GetEnvironmentVariable("REFRESH_ROTATION_TEST_PROVIDER") ?? "Sqlite";
        var databaseName = $"refresh_rotation_{Guid.NewGuid():N}";
        var connection = Environment.GetEnvironmentVariable("REFRESH_ROTATION_TEST_CONNECTION");
        string? path = null;
        if (provider == "Sqlite")
        {
            var directory = Path.Combine(Path.GetTempPath(), "opencode");
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, $"{databaseName}.db");
            connection = $"Data Source={path};Pooling=False;Default Timeout=5";
        }
        else if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("Production-provider tests require REFRESH_ROTATION_TEST_CONNECTION.");
        }

        // Never migrate or delete the database named by the caller: always use a fresh, owned database.
        connection = provider switch
        {
            "Sqlite" => connection,
            "SqlServer" => new SqlConnectionStringBuilder(connection) { InitialCatalog = databaseName, Pooling = false }.ConnectionString,
            "PostgreSql" => new NpgsqlConnectionStringBuilder(connection) { Database = databaseName, Pooling = false }.ConnectionString,
            _ => throw new InvalidOperationException($"Unsupported refresh rotation test provider '{provider}'.")
        };

        ServiceProvider CreateProvider()
        {
            var services = new ServiceCollection();
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                switch (provider)
                {
                    case "Sqlite":
                        options.UseSqlite(connection);
                        break;
                    case "SqlServer":
                        options.UseSqlServer(connection, sql => sql.MigrationsAssembly("CleanTemplate.Migrations.SqlServer"));
                        break;
                    case "PostgreSql":
                        options.UseNpgsql(connection, sql => sql.MigrationsAssembly("CleanTemplate.Migrations.PostgreSql"));
                        break;
                }
                options.AddInterceptors(interceptors);
            });
            return services.BuildServiceProvider();
        }

        var fixture = new RefreshTokenDatabaseFixture(CreateProvider(), CreateProvider(), path);
        try
        {
            await using var scope = fixture.First.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (provider == "Sqlite")
                await db.Database.EnsureCreatedAsync();
            else if (legacySchema)
                await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations()
                    .Single(name => name.EndsWith("_EnforceUniqueIdentityEmail", StringComparison.Ordinal)));
            else
                await db.Database.MigrateAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public async Task<T> ReadAsync<T>(Func<ApplicationDbContext, Task<T>> read)
    {
        await using var scope = First.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (sqlitePath is null)
            {
                await using var scope = first.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync();
            }
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
            if (sqlitePath is not null)
            {
                foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
                    File.Delete(sqlitePath + suffix);
            }
        }
    }
}
