using CleanTemplate.Application.Contracts;
using CleanTemplate.Application.Security;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Persistence;
using CleanTemplate.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Infrastructure.Tests.Testing;

internal sealed class IdentitySecurityFixture(SqliteConnection connection, ServiceProvider provider, AsyncServiceScope scope) : IAsyncDisposable
{
    public ApplicationDbContext Db => scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    public UserManager<ApplicationUser> Users => scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    public ExternalIdentityProvisioningService Provisioner => scope.ServiceProvider.GetRequiredService<ExternalIdentityProvisioningService>();
    public IUserAuthorizationService Authorization => scope.ServiceProvider.GetRequiredService<IUserAuthorizationService>();
    public IAuthUserService AuthUsers => scope.ServiceProvider.GetRequiredService<IAuthUserService>();
    public ManualTimeProvider Clock => scope.ServiceProvider.GetRequiredService<ManualTimeProvider>();

    public static async Task<IdentitySecurityFixture> CreateAsync(ExternalRoleMappingOptions? mapping = null, IInterceptor? interceptor = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ManualTimeProvider>();
        services.AddSingleton<TimeProvider>(provider => provider.GetRequiredService<ManualTimeProvider>());
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlite(connection);
            if (interceptor is not null)
            {
                options.AddInterceptors(interceptor);
            }
        });
        services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddSingleton<IOptions<ExternalRoleMappingOptions>>(Options.Create(mapping ?? new ExternalRoleMappingOptions()));
        services.AddScoped<IExternalRoleMapper, ExternalRoleMapper>();
        services.AddScoped<IUserAuthorizationService, UserAuthorizationService>();
        services.AddScoped<IAuthUserService, AuthUserService>();
        services.AddScoped<ExternalIdentityProvisioningService>();
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateAsyncScope();
        var fixture = new IdentitySecurityFixture(connection, provider, scope);
        await fixture.Db.Database.EnsureCreatedAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        Assert.True((await roles.CreateAsync(new IdentityRole<Guid>("Admin"))).Succeeded);
        Assert.True((await roles.CreateAsync(new IdentityRole<Guid>("User"))).Succeeded);
        return fixture;
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await provider.DisposeAsync();
        await connection.DisposeAsync();
    }
}
