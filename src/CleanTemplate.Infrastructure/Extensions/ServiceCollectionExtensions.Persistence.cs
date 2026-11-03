using CleanTemplate.Application.Contracts.Persistence;
using CleanTemplate.Infrastructure.Database;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.Infrastructure.Repositories.Reads;
using CleanTemplate.Infrastructure.Repositories.Writes;
using CleanTemplate.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CleanTemplate.Infrastructure.Extensions;

public static partial class ServiceCollectionExtensions
{
    private static void AddDatabase(IServiceCollection services, DatabaseProvider provider, string connectionString)
    {
        if (provider == DatabaseProvider.SqliteInMemory)
        {
            var sqliteOptions = new SqliteConnectionStringBuilder(connectionString);
            if (sqliteOptions.Mode != SqliteOpenMode.Memory || sqliteOptions.Cache != SqliteCacheMode.Shared
                || string.IsNullOrWhiteSpace(sqliteOptions.DataSource) || sqliteOptions.DataSource == ":memory:")
            {
                throw new InvalidOperationException("SqliteInMemory requires a named database with Mode=Memory;Cache=Shared for isolated request connections.");
            }

            services.AddSingleton(sp =>
            {
                var keepAliveConnection = new SqliteConnection(connectionString);
                keepAliveConnection.Open();

                var appLifetime = sp.GetService<IHostApplicationLifetime>();
                appLifetime?.ApplicationStopping.Register(() => keepAliveConnection.Close());

                return keepAliveConnection;
            });
        }

        services.AddScoped<PrivilegeAuditState>();
        services.AddScoped<PrivilegeSaveInterceptor>();
        services.AddScoped<PrivilegeTransactionInterceptor>();
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetRequiredService<PrivilegeSaveInterceptor>(), sp.GetRequiredService<PrivilegeTransactionInterceptor>());
            switch (provider)
            {
                case DatabaseProvider.SqlServer:
                    options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly("CleanTemplate.Migrations.SqlServer"));
                    break;
                case DatabaseProvider.PostgreSql:
                    options.UseNpgsql(connectionString, sql => sql.MigrationsAssembly("CleanTemplate.Migrations.PostgreSql"));
                    break;
                case DatabaseProvider.SqliteInMemory:
                    _ = sp.GetRequiredService<SqliteConnection>(); // Keep the named memory database alive, not a shared request connection.
                    options.UseSqlite(connectionString);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported database provider '{provider}'.");
            }
        });

    }

    private static void AddProductReads(IServiceCollection services, IConfiguration configuration, DatabaseProvider provider, string connectionString)
    {
        var readMode = configuration["ProductReads:Mode"] ?? "Primary";
        var replica = configuration.GetConnectionString("ProductReadReplica");
        var legacyReads = new[] { configuration.GetConnectionString("ReadConnection"), configuration.GetConnectionString("DapperConnection") };
        if (legacyReads.Any(value => !string.IsNullOrWhiteSpace(value) && value != connectionString))
        {
            throw new InvalidOperationException("Legacy ReadConnection/DapperConnection differs from DefaultConnection. Remove it and explicitly configure ProductReads:Mode=Replica and ProductReadReplica if eventual consistency is intended.");
        }

        string readConnectionString;
        if (string.Equals(readMode, "Primary", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(replica))
                throw new InvalidOperationException("ProductReadReplica requires ProductReads:Mode=Replica.");
            readConnectionString = connectionString;
        }
        else if (string.Equals(readMode, "Replica", StringComparison.OrdinalIgnoreCase))
        {
            if (provider == DatabaseProvider.SqliteInMemory || string.IsNullOrWhiteSpace(replica))
                throw new InvalidOperationException("Replica mode requires ProductReadReplica and a non-SQLite provider.");
            readConnectionString = replica;
        }
        else
        {
            throw new InvalidOperationException("ProductReads:Mode must be Primary or Replica.");
        }

        services.AddSingleton(new ReadDbConnectionFactory(readConnectionString, provider));
        services.AddSingleton<ISqlDialect>(provider switch
        {
            DatabaseProvider.SqlServer => new SqlServerDialect(),
            DatabaseProvider.PostgreSql => new PostgreSqlDialect(),
            DatabaseProvider.SqliteInMemory => new SqliteDialect(),
            _ => throw new InvalidOperationException($"Unsupported database provider '{provider}'.")
        });

    }

    private static void AddProductPersistence(IServiceCollection services)
    {
        services.AddScoped<IProductQueries, ProductReadRepository>();
        services.AddScoped<IProductWriteRepository, ProductWriteRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
    }
}
