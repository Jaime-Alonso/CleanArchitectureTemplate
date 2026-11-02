using CleanTemplate.Infrastructure.Caching.Options;
using CleanTemplate.Infrastructure.Database;
using CleanTemplate.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Infrastructure.Extensions;

public static partial class ServiceCollectionExtensions
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

        AddDatabase(services, provider, connectionString);
        AddIdentityOptions(services, configuration);
        AddCachingOptions(services, configuration);
        AddIdentity(services);
        var cacheOptions = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>() ?? new CacheOptions();
        services.AddScoped<IdentityDataSeeder>();
        AddCaching(services, cacheOptions);
        AddProductReads(services, configuration, provider, connectionString);
        AddProductPersistence(services);
        AddIdentityServices(services);

        return services;
    }
}
