using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CleanTemplate.Api.ExceptionHandling;

public sealed class JsonProblemDetailsWriter(IOptions<ProblemDetailsOptions> options) : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext context) => true;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        options.Value.CustomizeProblemDetails?.Invoke(context);
        return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
            context.ProblemDetails, context.ProblemDetails.GetType(), options: null,
            contentType: "application/problem+json", cancellationToken: context.HttpContext.RequestAborted));
    }
}
