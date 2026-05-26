using CleanTemplate.Crosscutting.Networking.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Crosscutting.Networking;

public static class CorsExtensions
{
    public static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration)
    {
        ApiCorsOptions corsOptions = configuration
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
                    .WithOrigins([.. corsOptions.AllowedOrigins])
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        return services;
    }
}
