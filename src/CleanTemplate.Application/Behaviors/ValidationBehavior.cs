using CleanTemplate.SharedKernel.Errors;
using FluentValidation;
using FluentValidation.Results;
using Mediora;

namespace CleanTemplate.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IValidator<TRequest>[] _validators;
    private readonly Func<IEnumerable<Error>, TResponse>? _failureFactory;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators.ToArray();
        if (_validators.Length > 0)
            _failureFactory = ValidationResponseFactory.Create<TResponse>();
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_validators.Length == 0)
            return await next().ConfigureAwait(false);

        var failures = new List<ValidationFailure>();
        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken)
                .ConfigureAwait(false);
            failures.AddRange(result.Errors.Where(f => f is not null));
        }

        if (failures.Count != 0)
            return CreateValidationFailureResponse(failures);

        return await next().ConfigureAwait(false);
    }

    private TResponse CreateValidationFailureResponse(IReadOnlyCollection<ValidationFailure> failures)
    {
        var errors = failures
            .Select(f => Error.Validation(
                code: $"Validation.{f.PropertyName}",
                description: f.ErrorMessage))
            .ToArray();

        return _failureFactory!(errors);
    }
}
