using System.Globalization;
using System.Reflection;

internal static class StartupPreflight
{
    public static void Check(IConfiguration configuration)
    {
        CheckSection(configuration.GetSection("Auth"), typeof(CleanTemplate.Api.Security.Options.AuthOptions));
        CheckSection(configuration.GetSection("Auth:RefreshTokenCleanup"), typeof(CleanTemplate.Infrastructure.Security.RefreshTokenCleanupOptions));
        CheckSection(configuration.GetSection("RateLimiting"), typeof(CleanTemplate.Crosscutting.RateLimiting.Options.ApiRateLimitingOptions));
        CheckSection(configuration.GetSection("Caching"), typeof(CleanTemplate.Infrastructure.Caching.Options.CacheOptions));
        CheckSection(configuration.GetSection("OpenTelemetry"), typeof(CleanTemplate.Crosscutting.Observability.Options.OpenTelemetryOptions));
    }

    private static void CheckSection(IConfigurationSection section, Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var child = section.GetSection(property.Name);
            var value = child.Value;
            if (value is not null && property.PropertyType == typeof(int)
                && !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                throw new StartupConfigurationException(child.Path);
            if (value is not null && property.PropertyType == typeof(bool) && !bool.TryParse(value, out _))
                throw new StartupConfigurationException(child.Path);
            if (property.PropertyType.IsClass && property.PropertyType != typeof(string))
                CheckSection(child, property.PropertyType);
        }
    }
}
