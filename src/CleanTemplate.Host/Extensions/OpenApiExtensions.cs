using Asp.Versioning;
using CleanTemplate.Api.Endpoints.Contracts.Auth;
using CleanTemplate.Api.Endpoints.Contracts.Products;
using CleanTemplate.Application.Products.Queries.GetProductById;
using CleanTemplate.Application.Products.Queries.GetProducts;
using CleanTemplate.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
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
        }).AddOpenApi(options =>
        {
            options.Document.AddDocumentTransformer(new ApiVersionDocumentTransformer(options.Description.GroupName));
            options.Document.AddOperationTransformer(TransformOperationAsync);
            options.Document.AddSchemaTransformer((schema, context, _) =>
            {
                if (context.JsonTypeInfo.Type == typeof(decimal))
                    schema.Format = "decimal";
                if (typeof(ProblemDetails).IsAssignableFrom(context.JsonTypeInfo.Type))
                {
                    schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
                    schema.Properties["code"] = new OpenApiSchema { Type = JsonSchemaType.String };
                    schema.Properties["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String };
                    schema.Required ??= new HashSet<string>();
                    schema.Required.Add("code");
                    schema.Required.Add("traceId");
                    if (typeof(HttpValidationProblemDetails).IsAssignableFrom(context.JsonTypeInfo.Type))
                        schema.Required.Add("errors");
                }
                if (context.JsonTypeInfo.Type == typeof(LoginResponse) && schema.Properties is not null)
                {
                    schema.Required ??= new HashSet<string>();
                    foreach (var property in schema.Properties.Keys) schema.Required.Add(property);
                }
                TransformProductSchema(schema, context.JsonTypeInfo.Type);
                return Task.CompletedTask;
            });
        });
        return services;
    }

    private static void TransformProductSchema(OpenApiSchema schema, Type type)
    {
        var request = type == typeof(CreateProductRequest) || type == typeof(UpdateProductRequest);
        var response = type == typeof(ProductDto) || type == typeof(ProductListItemDto);
        if ((!request && !response) || schema.Properties is null) return;

        if (schema.Properties["name"] is OpenApiSchema name)
        {
            name.MinLength = 1;
            name.MaxLength = Product.NameMaxLength;
            name.Description = "Must contain a non-whitespace character; maximum length is measured in UTF-16 code units.";
        }
        if (schema.Properties.TryGetValue("description", out var description) && description is OpenApiSchema text)
        {
            text.MaxLength = Product.DescriptionMaxLength;
            text.Description = "Maximum length is measured in UTF-16 code units.";
        }
        if (schema.Properties["price"] is OpenApiSchema price)
        {
            price.Type = JsonSchemaType.Number;
            price.Pattern = null;
            price.Minimum = "0";
            price.Maximum = Product.PriceMaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            price.MultipleOf = 0.01m;
        }
        if (schema.Properties["stock"] is OpenApiSchema stock)
        {
            stock.Type = JsonSchemaType.Integer;
            stock.Pattern = null;
            stock.Minimum = "0";
        }
        var versionName = request ? "expectedVersion" : "version";
        if (schema.Properties.TryGetValue(versionName, out var versionProperty) && versionProperty is OpenApiSchema version)
        {
            version.Type = JsonSchemaType.Integer;
            version.Pattern = null;
            version.Minimum = "1";
            schema.Required ??= new HashSet<string>();
            schema.Required.Add(versionName);
        }
        if (response)
        {
            schema.Required ??= new HashSet<string>();
            foreach (var property in schema.Properties.Keys) schema.Required.Add(property);
        }
    }

    private static async Task TransformOperationAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (operation.Parameters is not null)
        {
            foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>())
            {
                if (parameter.Name == "expectedVersion" && parameter.Schema is OpenApiSchema version)
                {
                    parameter.Required = true;
                    version.Type = JsonSchemaType.Integer;
                    version.Format = "int64";
                    version.Pattern = null;
                    version.Minimum = "1";
                }
            }
        }
        if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security = [new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
            }];
        }

        operation.Responses ??= new OpenApiResponses();
        var problemSchema = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), cancellationToken: cancellationToken);
        if (operation.Responses.TryGetValue("400", out var badRequest) && badRequest is OpenApiResponse badResponse &&
            badResponse.Content?.TryGetValue("application/problem+json", out var badContent) == true)
        {
            var validationSchema = await context.GetOrCreateSchemaAsync(typeof(HttpValidationProblemDetails), cancellationToken: cancellationToken);
            badContent.Schema = new OpenApiSchema { AnyOf = [problemSchema, validationSchema] };
        }
        operation.Responses["429"] = new OpenApiResponse
        {
            Description = "Rate limit exceeded. Retry-After is present when the limiter provides a retry time.",
            Headers = new Dictionary<string, IOpenApiHeader>
            {
                ["Retry-After"] = new OpenApiHeader { Description = "Seconds until retry, when available.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } }
            },
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new() { Schema = problemSchema }
            }
        };
        if (operation.Responses.TryGetValue("401", out var unauthorized) && unauthorized is OpenApiResponse response)
        {
            response.Headers ??= new Dictionary<string, IOpenApiHeader>();
            response.Headers["WWW-Authenticate"] = new OpenApiHeader
            {
                Description = "Bearer authentication challenge.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            };
        }
    }
}

file sealed class ApiVersionDocumentTransformer(string version) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "CleanTemplate API",
            Version = version,
            Description = $"CleanTemplate API - Version {version}",
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        };
        return Task.CompletedTask;
    }
}
