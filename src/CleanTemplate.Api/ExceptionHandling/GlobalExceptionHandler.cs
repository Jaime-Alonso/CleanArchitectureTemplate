using System.Net;
using CleanTemplate.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanTemplate.Api.ExceptionHandling;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private static readonly Dictionary<Type, (HttpStatusCode Status, string Title)> ExceptionMappings = new()
    {
        [typeof(DomainValidationException)] = (HttpStatusCode.BadRequest, "Domain validation error"),
        [typeof(DomainException)] = (HttpStatusCode.Conflict, "Domain rule conflict"),
        [typeof(InvalidOperationException)] = (HttpStatusCode.InternalServerError, "Invalid operation"),
        [typeof(NotSupportedException)] = (HttpStatusCode.BadRequest, "Operation not supported"),
        [typeof(TimeoutException)] = (HttpStatusCode.GatewayTimeout, "Request timed out"),
    };

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IWebHostEnvironment environment, ILogger<GlobalExceptionHandler> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        LogException(exception);

        var (statusCode, title) = GetStatusCodeAndTitle(exception);
        var problemDetails = CreateProblemDetails(httpContext, exception, statusCode, title);

        httpContext.Response.StatusCode = problemDetails.Status ?? (int)statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private void LogException(Exception exception)
    {
        _logger.LogError(
            exception,
            "An unhandled exception occurred: {ExceptionType} - {Message}",
            exception.GetType().Name,
            exception.Message);
    }

    private static (HttpStatusCode statusCode, string title) GetStatusCodeAndTitle(Exception exception)
    {
        var exceptionType = exception.GetType();

        foreach (var (type, mapping) in ExceptionMappings)
        {
            if (type.IsAssignableFrom(exceptionType))
            {
                return mapping;
            }
        }

        return (HttpStatusCode.InternalServerError, "An unexpected error occurred");
    }

    private ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        Exception exception,
        HttpStatusCode statusCode,
        string title)
    {
        var problemDetails = new ProblemDetails
        {
            Status = (int)statusCode,
            Title = title,
            Type = GetProblemDetailsType(statusCode),
            Instance = httpContext.Request.Path,
        };

        if (_environment.IsDevelopment())
        {
            problemDetails.Detail = exception.Message;
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
            problemDetails.Extensions["exceptionType"] = exception.GetType().Name;
        }

        if (exception is DomainException domainException)
        {
            problemDetails.Extensions["code"] = domainException.Code;
            problemDetails.Detail ??= domainException.Message;
        }

        return problemDetails;
    }

    private static string GetProblemDetailsType(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            HttpStatusCode.Unauthorized => "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            HttpStatusCode.Forbidden => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
            HttpStatusCode.NotFound => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
            HttpStatusCode.Conflict => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            HttpStatusCode.GatewayTimeout => "https://tools.ietf.org/html/rfc9110#section-15.6.5",
            _ => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        };
    }
}
