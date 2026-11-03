using CleanTemplate.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CleanTemplate.Infrastructure.Extensions;

public static class IdentityInitializationExtensions
{
    public static async Task InitializeIdentityAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<Persistence.ApplicationDbContext>();
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (!dbContext.Database.IsSqlite())
            return;

        var isDevelopmentOrTesting = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var productionOptIn = environment.IsProduction()
            && bool.TryParse(configuration["Database:AllowEphemeralInProduction"], out var enabled) && enabled;
        if (!isDevelopmentOrTesting && !productionOptIn)
            return;

        await dbContext.InitializeDatabaseAsync(true, cancellationToken).ConfigureAwait(false);

        if (!isDevelopmentOrTesting)
            return;

        if (await dbContext.Users.AnyAsync(cancellationToken).ConfigureAwait(false))
            return;

        var seeder = scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>();
        await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);
    }
}
