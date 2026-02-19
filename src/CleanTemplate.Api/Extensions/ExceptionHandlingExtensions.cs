using Microsoft.AspNetCore.Builder;
using CleanTemplate.Api.ExceptionHandling;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Api.Extensions;

public static class ExceptionHandlingExtensions
{
    public static IServiceCollection AddGlobalErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }

    public static IApplicationBuilder UseGlobalErrorHandling(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();

        return app;
    }
}
