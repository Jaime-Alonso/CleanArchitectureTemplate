using CleanTemplate.CrossCutting.Networking.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.CrossCutting.Networking;

public static class CorsExtensions
{
    public static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration)
    {
        var corsOptions = configuration
            .GetSection(ApiCorsOptions.SectionName)
            .Get<ApiCorsOptions>()
            ?? new ApiCorsOptions();

        services.AddCors(options =>
        {
            options.AddPolicy(ApiCorsOptions.PolicyName, policy =>
            {
                if (corsOptions.AllowedOrigins.Count == 0)
                {
                    return;
                }

                policy
                    .WithOrigins(corsOptions.AllowedOrigins.ToArray())
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        return services;
    }
}
