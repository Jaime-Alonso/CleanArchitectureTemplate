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

        foreach (string origin in corsOptions.AllowedOrigins)
        {
            if (string.IsNullOrWhiteSpace(origin) || origin != origin.Trim() || origin.Contains('*') || origin.Contains('@') ||
                !Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
                origin.IndexOfAny(['/', '?', '#'], origin.IndexOf("://", StringComparison.Ordinal) + 3) >= 0)
                throw new InvalidOperationException("Cors:AllowedOrigins contains an invalid HTTP(S) origin.");
        }

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
