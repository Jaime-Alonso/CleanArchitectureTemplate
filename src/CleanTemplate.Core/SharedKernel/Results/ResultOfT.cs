using CleanTemplate.Core.SharedKernel.Errors;

namespace CleanTemplate.Core.SharedKernel.Results;

public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue value)
        : base(true, [])
    {
        _value = value;
    }

    private Result(IEnumerable<Error> errors)
        : base(false, errors)
    {
    }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    public static Result<TValue> Success(TValue value)
    {
        return new Result<TValue>(value);
    }

    public static new Result<TValue> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<TValue>([error]);
    }

    public static new Result<TValue> Failure(IEnumerable<Error> errors)
    {
        return new Result<TValue>(errors);
    }
}
