using System.Diagnostics;
using CleanTemplate.Api.ExceptionHandling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CleanTemplate.Api.Extensions;

public static class ExceptionHandlingExtensions
{
    public static IServiceCollection AddGlobalErrorHandling(this IServiceCollection services)
    {
        services.AddSingleton<IProblemDetailsWriter, JsonProblemDetailsWriter>();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
            context.ProblemDetails.Extensions["code"] = status switch
            {
                400 => "request.invalid",
                401 => "auth.unauthorized",
                403 => "auth.forbidden",
                404 => "resource.not_found",
                409 => "resource.conflict",
                429 => "rate_limit.exceeded",
                >= 500 => "server.error",
                _ => "request.error"
            };
            context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        return services;
    }

    public static IApplicationBuilder UseGlobalErrorHandling(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages(async context =>
        {
            var httpContext = context.HttpContext;
            if (!httpContext.Response.HasStarted && !httpContext.RequestAborted.IsCancellationRequested)
            {
                var result = httpContext.Response.StatusCode == StatusCodes.Status400BadRequest
                    ? Results.ValidationProblem(new Dictionary<string, string[]> { [""] = ["The request is invalid."] })
                    : Results.Problem(statusCode: httpContext.Response.StatusCode);
                await result.ExecuteAsync(httpContext).ConfigureAwait(false);
            }
        });

        return app;
    }
}
