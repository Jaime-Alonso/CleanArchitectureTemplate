using CleanTemplate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CleanTemplate.Migrations.SqlServer;

public sealed class SqlServerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Scaffolding and script generation do not connect to this database.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost;Database=CleanTemplate;Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(SqlServerDesignTimeDbContextFactory).Assembly.FullName))
            .Options;
        return new ApplicationDbContext(options);
    }
}
