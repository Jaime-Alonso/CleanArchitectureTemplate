using CleanTemplate.Application.Contracts.Identity;
using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Extensions;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.Infrastructure.Security;
using CleanTemplate.SharedKernel.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Tests.Testing;

/// <summary>Two independent containers and per-context connections to one isolated relational database.</summary>
internal sealed class ProvisioningDatabaseFixture(string path, ServiceProvider first, ServiceProvider second) : IAsyncDisposable
{
    public ServiceProvider First => first;
    public ServiceProvider Second => second;

    public static async Task<ProvisioningDatabaseFixture> CreateAsync(params IInterceptor[] interceptors)
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"provisioning-{Guid.NewGuid():N}.db");

        ServiceProvider CreateProvider()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddDbContext<ApplicationDbContext>(options => options
                .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=1")
                .AddInterceptors(interceptors));
            services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
                .AddRoles<IdentityRole<Guid>>()
                .AddEntityFrameworkStores<ApplicationDbContext>();
            services.AddSingleton<IOptions<ExternalRoleMappingOptions>>(Options.Create(new ExternalRoleMappingOptions
            {
                Admin = new ExternalRoleMappingRuleOptions { Roles = ["external-admin"] }
            }));
            services.AddScoped<IExternalRoleMapper, ExternalRoleMapper>();
            services.AddScoped<IUserAuthorizationService, UserAuthorizationService>();
            services.AddExternalIdentityProvisioning();
            return services.BuildServiceProvider();
        }

        var fixture = new ProvisioningDatabaseFixture(path, CreateProvider(), CreateProvider());
        try
        {
            await using var scope = fixture.First.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Database.EnsureCreatedAsync();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>("Admin"))).Succeeded);
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>("User"))).Succeeded);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public async Task<Result<ProvisionResult>> ProvisionAsync(ServiceProvider instance, ExternalIdentity identity, CancellationToken token = default)
    {
        await using var scope = instance.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IExternalIdentityProvisioningService>().ProvisionAsync(identity, token);
    }

    public async Task<T> ReadAsync<T>(Func<ApplicationDbContext, Task<T>> read)
    {
        await using var scope = First.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        await first.DisposeAsync();
        await second.DisposeAsync();
        // Only files created by this uniquely named test database are removed.
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
        {
            File.Delete(path + suffix);
        }
    }
}
