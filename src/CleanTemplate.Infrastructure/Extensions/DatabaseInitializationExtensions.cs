using CleanTemplate.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CleanTemplate.Infrastructure.Extensions;

public static class DatabaseInitializationExtensions
{
    public static async Task InitializeDatabaseAsync(
        this ApplicationDbContext dbContext,
        bool allowEphemeralDatabase,
        CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.IsSqlite())
        {
            var connection = new SqliteConnectionStringBuilder(dbContext.Database.GetConnectionString());
            if (!allowEphemeralDatabase || (connection.Mode != SqliteOpenMode.Memory && connection.DataSource != ":memory:"))
            {
                throw new InvalidOperationException(
                    "SQLite schema creation is only supported for in-memory databases in Development or Testing. " +
                    "Use SQL Server or PostgreSQL with versioned migrations for persistent databases.");
            }

            await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!dbContext.Database.GetMigrations().Any())
        {
            throw new InvalidOperationException("No versioned migrations were found for the configured database provider.");
        }

        // Called explicitly by the deployment migration operation.
        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
