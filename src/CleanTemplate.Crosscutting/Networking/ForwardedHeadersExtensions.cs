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

        var proxies = new List<IPAddress>();
        foreach (string knownProxy in trustOptions.KnownProxies)
        {
            if (string.IsNullOrWhiteSpace(knownProxy) || !IPAddress.TryParse(knownProxy, out IPAddress? proxy))
                throw new InvalidOperationException("ForwardedHeaders:KnownProxies contains an invalid IP address.");
            proxies.Add(proxy);
        }

        var networks = new List<System.Net.IPNetwork>();
        foreach (string knownNetwork in trustOptions.KnownNetworks)
        {
            if (string.IsNullOrWhiteSpace(knownNetwork) || !System.Net.IPNetwork.TryParse(knownNetwork, out var network) ||
                !IPAddress.TryParse(knownNetwork.Split('/')[0], out IPAddress? address) || !address.Equals(network.BaseAddress))
                throw new InvalidOperationException("ForwardedHeaders:KnownNetworks contains an invalid CIDR network.");
            networks.Add(network);
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            foreach (IPAddress proxy in proxies)
            {
                options.KnownProxies.Add(proxy);
            }

            foreach (System.Net.IPNetwork network in networks)
            {
                options.KnownIPNetworks.Add(network);
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
