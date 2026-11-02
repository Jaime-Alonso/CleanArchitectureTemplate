using Scalar.AspNetCore;

namespace CleanTemplate.Host.Extensions;

public static class ScalarExtensions
{
    public static WebApplication UseScalarDocumentation(this WebApplication app)
    {
        app.MapScalarApiReference(options =>
        {
            options
                .WithTitle("CleanTemplate API")
                .WithOpenApiRoutePattern("/openapi/{documentName}.json")
                .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
        });

        return app;
    }
}
