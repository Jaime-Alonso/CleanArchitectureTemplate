using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

namespace CleanTemplate.Host.Extensions;

public static class OpenApiExtensions
{
    public static IServiceCollection AddApiContract(this IServiceCollection services, IApiVersioningBuilder versioning)
    {
        versioning.AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = false;
        });
        // ApiExplorer assigns operations to groups from endpoint version metadata.
        // Register a native document for each supported version, never per endpoint.
        services.AddOpenApi("v1", options =>
        {
            options.ShouldInclude = description => description.GroupName == "v1";
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
                    operation.Security = [new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
                    }];
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo { Title = "CleanTemplate API", Version = "v1" };
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT"
                };
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
