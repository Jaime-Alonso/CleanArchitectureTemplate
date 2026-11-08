using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

[assembly: HostingStartup(typeof(CleanTemplate.Api.IntegrationTests.Infrastructure.TestConfigurationStartup))]

namespace CleanTemplate.Api.IntegrationTests.Infrastructure;

// Loaded only by the fixture's explicit hostingStartupAssemblies setting. No runtime test hook.
public sealed class TestConfigurationStartup : IHostingStartup
{
    internal static AsyncLocal<IReadOnlyDictionary<string, string?>?> Settings { get; } = new();

    public void Configure(IWebHostBuilder builder)
    {
        var settings = Settings.Value ?? throw new InvalidOperationException("Missing HTTP fixture configuration.");
        // Hosting startup runs inside CreateBuilder, before Program validates or registers services.
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(settings);
        });
    }
}
