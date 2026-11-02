using CleanTemplate.Infrastructure.Identity;
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
        await dbContext.InitializeDatabaseAsync(
            environment.IsDevelopment() || environment.IsEnvironment("Testing"),
            cancellationToken).ConfigureAwait(false);

        var seeder = scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>();
        await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);
    }
}
