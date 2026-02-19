using Asp.Versioning;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Api.Extensions;

public static class ApiVersioningExtensions
{
    public static IApiVersioningBuilder AddApiVersioningConfiguration(this IServiceCollection services)
    {
        return services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            options.ApiVersionReader = new HeaderApiVersionReader("X-Api-Version");
        });
    }
}
