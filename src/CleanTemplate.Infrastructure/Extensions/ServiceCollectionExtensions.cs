using System;
using CleanTemplate.Application.Abstractions;
using CleanTemplate.Infrastructure.Database;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.Infrastructure.Repositories.Reads;
using CleanTemplate.Infrastructure.Security;
using CleanTemplate.Infrastructure.Security.Options;
using CleanTemplate.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CleanTemplate.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = DatabaseProviderParser.Parse(configuration["DatabaseProvider"]);
        var configuredConnectionString = configuration.GetConnectionString("DefaultConnection");
        var connectionString = provider == DatabaseProvider.SqliteInMemory
            ? configuredConnectionString ?? "Data Source=CleanTemplateInMemory;Mode=Memory;Cache=Shared"
            : configuredConnectionString ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        if (provider == DatabaseProvider.SqliteInMemory)
        {
            services.AddSingleton(sp =>
            {
                var keepAliveConnection = new SqliteConnection(connectionString);
                keepAliveConnection.Open();

                var appLifetime = sp.GetService<IHostApplicationLifetime>();
                appLifetime?.ApplicationStopping.Register(() => keepAliveConnection.Close());

                return keepAliveConnection;
            });
        }

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            switch (provider)
            {
                case DatabaseProvider.SqlServer:
                    options.UseSqlServer(connectionString);
                    break;
                case DatabaseProvider.PostgreSql:
                    options.UseNpgsql(connectionString);
                    break;
                case DatabaseProvider.SqliteInMemory:
                    options.UseSqlite(sp.GetRequiredService<SqliteConnection>());
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported database provider '{provider}'.");
            }
        });

        services
            .AddOptions<ExternalRoleMappingOptions>()
            .Bind(configuration.GetSection("Auth:Provisioning"))
            .ValidateOnStart();

        services
            .AddOptions<RefreshTokenOptions>()
            .Bind(configuration.GetSection("Auth:Schemes:LocalJwt"))
            .ValidateOnStart();

        services
            .AddOptions<LocalJwtOptions>()
            .Bind(configuration.GetSection("Auth:Schemes:LocalJwt"))
            .ValidateOnStart();

        services
            .AddOptions<RefreshTokenCleanupOptions>()
            .Bind(configuration.GetSection(RefreshTokenCleanupOptions.SectionName))
            .ValidateOnStart();

        services
            .AddOptions<IdentitySeedOptions>()
            .Bind(configuration.GetSection("IdentitySeed"))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.AdminEmail),
                "IdentitySeed:AdminEmail must be configured.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.AdminPassword),
                "IdentitySeed:AdminPassword must be configured via user-secrets or environment variables.")
            .Validate(
                options => !options.AdminPassword.StartsWith("__SET_VIA_", StringComparison.Ordinal),
                "IdentitySeed:AdminPassword is using a placeholder value. Configure a real password via user-secrets or environment variables.")
            .ValidateOnStart();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 8;
                options.Password.RequiredUniqueChars = 4;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<IdentityDataSeeder>();
        services.AddMemoryCache();

        var readConnectionString = configuration.GetConnectionString("ReadConnection")
            ?? configuration.GetConnectionString("DapperConnection")
            ?? connectionString;

        services.AddSingleton(new ReadDbConnectionFactory(readConnectionString, provider));
        services.AddSingleton<ISqlDialect>(provider switch
        {
            DatabaseProvider.SqlServer => new SqlServerDialect(),
            DatabaseProvider.PostgreSql => new PostgreSqlDialect(),
            DatabaseProvider.SqliteInMemory => new SqliteDialect(),
            _ => throw new InvalidOperationException($"Unsupported database provider '{provider}'.")
        });

        services.AddScoped<IProductReadRepository, ProductReadRepository>();
        services.AddScoped<IExternalRoleMapper, ExternalRoleMapper>();
        services.AddScoped<IExternalIdentityProvisioningService, ExternalIdentityProvisioningService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IAuthUserService, AuthUserService>();
        services.AddScoped<ILocalJwtTokenGenerator, LocalJwtTokenGenerator>();
        services.AddHostedService<RefreshTokenCleanupBackgroundService>();

        return services;
    }
}
