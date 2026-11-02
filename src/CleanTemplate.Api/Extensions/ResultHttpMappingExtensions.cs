using CleanTemplate.Api.ExceptionHandling;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
using Microsoft.AspNetCore.Http;

namespace CleanTemplate.Api.Extensions;

public static class ResultHttpMappingExtensions
{
    public static IResult ToHttpErrorResult(this Result result, Func<IReadOnlyList<Error>, IResult>? fallback = null)
    {
        if (result.Errors.Any(error => error.Type == ErrorType.Failure))
            return fallback is null ? Results.Problem(statusCode: StatusCodes.Status500InternalServerError) : fallback(result.Errors);

        if (result.Errors.Any(error => error.Type == ErrorType.Validation))
        {
            var errors = result.Errors.Where(error => error.Type == ErrorType.Validation)
                .GroupBy(error => error.Code.StartsWith("Validation.", StringComparison.Ordinal) ? error.Code[11..] : "")
                .ToDictionary(group => group.Key, _ => new[] { "The value is invalid." });
            return Results.ValidationProblem(errors);
        }

        if (result.Errors.Any(error => error.Type == ErrorType.NotFound))
            return Results.Problem(statusCode: StatusCodes.Status404NotFound);
        if (result.Errors.Any(error => error.Type == ErrorType.Conflict))
            return Results.Problem(statusCode: StatusCodes.Status409Conflict);
        if (result.Errors.Any(error => error.Type == ErrorType.Forbidden))
            return Results.Forbid();
        if (result.Errors.Any(error => error.Type == ErrorType.Unauthorized))
            return new BearerUnauthorizedResult();

        return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
    }

    public static IResult ToHttpErrorResult<TValue>(this Result<TValue> result, Func<IReadOnlyList<Error>, IResult>? fallback = null)
        => ((Result)result).ToHttpErrorResult(fallback);
}
