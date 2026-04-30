using FluentValidation;
using FluentValidation.Results;
using Mediora;
using CleanTemplate.Core.SharedKernel.Errors;
using CleanTemplate.Core.SharedKernel.Results;

namespace CleanTemplate.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next().ConfigureAwait(false);

        var context = new ValidationContext<TRequest>(request);
        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken))).ConfigureAwait(false);

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count != 0)
            return CreateValidationFailureResponse(failures);

        return await next().ConfigureAwait(false);
    }

    private static TResponse CreateValidationFailureResponse(IReadOnlyCollection<ValidationFailure> failures)
    {
        var errors = failures
            .Select(f => Error.Validation(
                code: $"Validation.{f.PropertyName}",
                description: f.ErrorMessage))
            .ToArray();

        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(errors);
        }

        var genericType = typeof(TResponse);
        if (genericType.IsGenericType && genericType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failureMethod = genericType.GetMethod(
                "Failure",
                [typeof(IEnumerable<Error>)])
                ?? throw new InvalidOperationException($"Could not find Failure(IEnumerable<Error>) on {genericType.Name}.");

            var failureResult = failureMethod.Invoke(null, [errors])
                ?? throw new InvalidOperationException($"Could not create validation failure result for {genericType.Name}.");

            return (TResponse)failureResult;
        }

        throw new InvalidOperationException($"ValidationBehavior requires response type Result or Result<T>. Received {typeof(TResponse).FullName}.");
    }
}
