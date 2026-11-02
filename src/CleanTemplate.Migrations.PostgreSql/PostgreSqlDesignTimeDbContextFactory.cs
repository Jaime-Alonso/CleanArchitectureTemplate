using CleanTemplate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CleanTemplate.Migrations.PostgreSql;

public sealed class PostgreSqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Scaffolding and script generation do not connect to this database.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Database=CleanTemplate;Username=postgres";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, sql => sql.MigrationsAssembly(typeof(PostgreSqlDesignTimeDbContextFactory).Assembly.FullName))
            .Options;
        return new ApplicationDbContext(options);
    }
}
