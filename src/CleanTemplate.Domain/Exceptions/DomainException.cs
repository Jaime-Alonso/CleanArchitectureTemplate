namespace CleanTemplate.Domain.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string code, string message)
        : base(message)
    {
        Code = string.IsNullOrWhiteSpace(code)
            ? throw new ArgumentException("Code cannot be empty.", nameof(code))
            : code;
    }

    public string Code { get; }
}
