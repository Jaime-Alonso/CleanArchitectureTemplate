using System.Net;
using CleanTemplate.Crosscutting.Networking.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Crosscutting.Networking;

public static class ForwardedHeadersExtensions
{
    public static IServiceCollection AddForwardedHeadersSupport(this IServiceCollection services, IConfiguration configuration)
    {
        ForwardedHeadersTrustOptions trustOptions = configuration
            .GetSection(ForwardedHeadersTrustOptions.SectionName)
            .Get<ForwardedHeadersTrustOptions>()
            ?? new ForwardedHeadersTrustOptions();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            foreach (string knownProxy in trustOptions.KnownProxies)
            {
                if (IPAddress.TryParse(knownProxy, out IPAddress? parsedProxy))
                {
                    options.KnownProxies.Add(parsedProxy);
                }
            }

            foreach (string knownNetwork in trustOptions.KnownNetworks)
            {
                string[] parts = knownNetwork.Split('/');
                if (parts.Length != 2)
                {
                    continue;
                }

                if (!IPAddress.TryParse(parts[0], out IPAddress? prefix))
                {
                    continue;
                }

                if (!int.TryParse(parts[1], out int prefixLength))
                {
                    continue;
                }

                options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, prefixLength));
            }
        });

        return services;
    }

    public static WebApplication UseConfiguredForwardedHeaders(this WebApplication app)
    {
        app.UseForwardedHeaders();
        return app;
    }
}
