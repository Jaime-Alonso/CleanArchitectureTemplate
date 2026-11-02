using Microsoft.AspNetCore.Http;

namespace CleanTemplate.Api.ExceptionHandling;

public sealed class BearerUnauthorizedResult : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        if (!httpContext.Response.Headers.WWWAuthenticate.Any(value =>
            value is not null && value.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase)))
        {
            httpContext.Response.Headers.Append("WWW-Authenticate", "Bearer");
        }

        return Results.Unauthorized().ExecuteAsync(httpContext);
    }
}
