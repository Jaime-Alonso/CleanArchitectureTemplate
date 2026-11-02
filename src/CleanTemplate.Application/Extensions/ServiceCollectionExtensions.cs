using CleanTemplate.Application.Behaviors;
using FluentValidation;
using Mediora;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(ServiceCollectionExtensions).Assembly;

        services.AddMediora(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
        });

        services.AddValidatorsFromAssembly(assembly);
        services.ValidateRequestValidationConfiguration();

        return services;
    }

    // Call again after registering additional validators, before building the provider.
    public static IServiceCollection ValidateRequestValidationConfiguration(this IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            var serviceType = descriptor.ServiceType;
            if (!serviceType.IsGenericType || serviceType.GetGenericTypeDefinition() != typeof(IValidator<>))
                continue;

            var requestType = serviceType.GenericTypeArguments[0];
            foreach (var requestInterface in requestType.GetInterfaces().Where(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            {
                var responseType = requestInterface.GenericTypeArguments[0];
                if (!ValidationResponseFactory.IsSupported(responseType))
                    throw new InvalidOperationException($"Validated request {requestType.FullName} requires response type Result or Result<T>; received {responseType.FullName}.");
            }
        }

        return services;
    }
}
