using System.Net;
using CleanTemplate.Core.CrossCutting.Networking.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Core.CrossCutting.Networking;

public static class ForwardedHeadersExtensions
{
    public static IServiceCollection AddForwardedHeadersSupport(this IServiceCollection services, IConfiguration configuration)
    {
        var trustOptions = configuration
            .GetSection(ForwardedHeadersTrustOptions.SectionName)
            .Get<ForwardedHeadersTrustOptions>()
            ?? new ForwardedHeadersTrustOptions();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            foreach (var knownProxy in trustOptions.KnownProxies)
            {
                if (IPAddress.TryParse(knownProxy, out var parsedProxy))
                {
                    options.KnownProxies.Add(parsedProxy);
                }
            }

            foreach (var knownNetwork in trustOptions.KnownNetworks)
            {
                var parts = knownNetwork.Split('/');
                if (parts.Length != 2)
                {
                    continue;
                }

                if (!IPAddress.TryParse(parts[0], out var prefix))
                {
                    continue;
                }

                if (!int.TryParse(parts[1], out var prefixLength))
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
