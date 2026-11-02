using System.Text.Json;
using CleanTemplate.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Api.ExceptionHandling;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted || httpContext.RequestAborted.IsCancellationRequested)
            return false;

        logger.LogError("Request {TraceId} failed with {ExceptionType}", httpContext.TraceIdentifier, exception.GetType().Name);
        var status = exception switch
        {
            DomainValidationException => StatusCodes.Status400BadRequest,
            DomainException => StatusCodes.Status409Conflict,
            BadHttpRequestException badRequest => badRequest.StatusCode,
            _ => StatusCodes.Status500InternalServerError
        };
        httpContext.Response.StatusCode = status;
        if (status == StatusCodes.Status400BadRequest)
        {
            var path = (exception.InnerException as JsonException)?.Path;
            var field = exception is DomainValidationException domain ? domain.Code switch
            {
                "Products.Name.Empty" or "Products.Name.TooLong" => "Name",
                "Products.Description.TooLong" => "Description",
                "Products.Price.Negative" or "Products.Price.Invalid" => "Price",
                "Products.Stock.Negative" => "Stock",
                _ => ""
            } : path is { Length: > 2 } && path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : "";
            await Results.ValidationProblem(new Dictionary<string, string[]> { [field] = ["The request is invalid."] })
                .ExecuteAsync(httpContext).ConfigureAwait(false);
        }
        else
        {
            await Results.Problem(statusCode: status).ExecuteAsync(httpContext).ConfigureAwait(false);
        }
        return true;
    }
}
