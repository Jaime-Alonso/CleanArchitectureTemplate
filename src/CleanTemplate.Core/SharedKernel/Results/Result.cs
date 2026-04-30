using CleanTemplate.Core.SharedKernel.Errors;

namespace CleanTemplate.Core.SharedKernel.Results;

public class Result
{
    private readonly List<Error> _errors;

    protected Result(bool isSuccess, IEnumerable<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        _errors = errors.ToList();

        if (isSuccess && _errors.Count != 0)
            throw new ArgumentException("A successful result cannot contain errors.", nameof(errors));

        if (!isSuccess && _errors.Count == 0)
            throw new ArgumentException("A failed result must contain at least one error.", nameof(errors));

        if (_errors.Any(error => error is null))
            throw new ArgumentException("Result errors cannot contain null values.", nameof(errors));

        IsSuccess = isSuccess;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public IReadOnlyList<Error> Errors => _errors;

    public Error? FirstError => _errors.Count > 0 ? _errors[0] : null;

    public static Result Success()
    {
        return new Result(true, []);
    }

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(false, [error]);
    }

    public static Result Failure(IEnumerable<Error> errors)
    {
        return new Result(false, errors);
    }
}
