namespace CleanTemplate.Domain.Exceptions;

public abstract class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = string.IsNullOrWhiteSpace(code)
            ? throw new ArgumentException("Code cannot be empty.", nameof(code))
            : code;
}
