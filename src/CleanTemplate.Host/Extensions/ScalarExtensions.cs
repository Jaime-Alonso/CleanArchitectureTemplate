using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace CleanTemplate.Host.Extensions;

public static class ScalarExtensions
{
    public static IServiceCollection AddScalarDocumentation(
        this IServiceCollection services,
        IApiVersioningBuilder apiVersioningBuilder)
    {
        apiVersioningBuilder.AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = false;
        });

        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer(new ApiVersionDocumentTransformer("v1"));
        });

        services.AddOpenApi("v2", options =>
        {
            options.AddDocumentTransformer(new ApiVersionDocumentTransformer("v2"));
        });

        return services;
    }

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

file class ApiVersionDocumentTransformer : IOpenApiDocumentTransformer
{
    private readonly string _version;

    public ApiVersionDocumentTransformer(string version)
    {
        _version = version;
    }

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "CleanTemplate API",
            Version = _version,
            Description = $"CleanTemplate API - Version {_version}",
        };

        return Task.CompletedTask;
    }
}
