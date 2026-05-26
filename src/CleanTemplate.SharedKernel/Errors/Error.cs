namespace CleanTemplate.SharedKernel.Errors;

public sealed class Error : IEquatable<Error>
{
    public string Code { get; }
    public string Description { get; }
    public ErrorType Type { get; }

    public Error(string code, string description)
        : this(code, description, ErrorType.Failure)
    {
    }

    public Error(string code, string description, ErrorType type)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Error code cannot be empty.", nameof(code));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Error description cannot be empty.", nameof(description));

        Code = code;
        Description = description;
        Type = type;
    }

    public static Error Validation(string code, string description)
    {
        return new(code, description, ErrorType.Validation);
    }

    public static Error NotFound(string code, string description)
    {
        return new(code, description, ErrorType.NotFound);
    }

    public static Error Failure(string code, string description)
    {
        return new(code, description, ErrorType.Failure);
    }

    public bool Equals(Error? other)
    {
        if (ReferenceEquals(null, other))
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return Code == other.Code && Description == other.Description && Type == other.Type;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Error);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Code, Description, Type);
    }

    public static bool operator ==(Error? left, Error? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(Error? left, Error? right)
    {
        return !Equals(left, right);
    }
}
