using CleanTemplate.Application.Contracts;
using CleanTemplate.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Infrastructure.Extensions;

public static class ExternalIdentityProvisioningExtensions
{
    /// <summary>Registers isolated provisioning attempts; Identity and persistence adapters must also be registered.</summary>
    public static IServiceCollection AddExternalIdentityProvisioning(this IServiceCollection services)
    {
        services.AddScoped<ExternalIdentityProvisioningAttempt>();
        services.AddScoped<ExternalIdentityProvisioningService>();
        services.AddScoped<IExternalIdentityProvisioningService>(provider => provider.GetRequiredService<ExternalIdentityProvisioningService>());
        return services;
    }
}
