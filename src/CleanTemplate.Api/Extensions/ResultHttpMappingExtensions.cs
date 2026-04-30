using CleanTemplate.Core.SharedKernel.Errors;
using CleanTemplate.Core.SharedKernel.Results;
using Microsoft.AspNetCore.Http;

namespace CleanTemplate.Api.Extensions;

public static class ResultHttpMappingExtensions
{
    public static IResult ToHttpErrorResult(
        this Result result,
        Func<IReadOnlyList<Error>, IResult>? fallback = null)
    {
        if (result.Errors.Any(error => error.Type == ErrorType.Validation))
        {
            return Results.BadRequest(new { Errors = result.Errors });
        }

        if (result.Errors.Any(error => error.Type == ErrorType.NotFound))
        {
            return Results.NotFound(new { Errors = result.Errors });
        }

        if (result.Errors.Any(error => error.Type == ErrorType.Conflict))
        {
            return Results.Conflict(new { Errors = result.Errors });
        }

        if (result.Errors.Any(error => error.Type == ErrorType.Forbidden))
        {
            return Results.Forbid();
        }

        if (result.Errors.Any(error => error.Type == ErrorType.Unauthorized))
        {
            return Results.Unauthorized();
        }

        return fallback is null
            ? Results.BadRequest(new { Errors = result.Errors })
            : fallback(result.Errors);
    }

    public static IResult ToHttpErrorResult<TValue>(
        this Result<TValue> result,
        Func<IReadOnlyList<Error>, IResult>? fallback = null)
    {
        return ((Result)result).ToHttpErrorResult(fallback);
    }
}
