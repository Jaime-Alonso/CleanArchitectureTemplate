namespace CleanTemplate.SharedKernel.Guards;

public static class Guard
{
    public static T AgainstNull<T>(T? value, string paramName) where T : class
    {
        return value is null ? throw new ArgumentNullException(paramName) : value;
    }

    public static string AgainstNullOrWhiteSpace(string? value, string paramName)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be null or whitespace.", paramName) : value;
    }

    public static Guid AgainstEmpty(Guid value, string paramName)
    {
        return value == Guid.Empty ? throw new ArgumentException("Guid cannot be empty.", paramName) : value;
    }

    public static int AgainstNegative(int value, string paramName)
    {
        return value < 0 ? throw new ArgumentOutOfRangeException(paramName, "Value cannot be negative.") : value;
    }

    public static decimal AgainstNegative(decimal value, string paramName)
    {
        return value < 0 ? throw new ArgumentOutOfRangeException(paramName, "Value cannot be negative.") : value;
    }

    public static int AgainstOutOfRange(int value, int minimum, int maximum, string paramName)
    {
        return value < minimum || value > maximum
            ? throw new ArgumentOutOfRangeException(paramName, $"Value must be between {minimum} and {maximum}.")
            : value;
    }
}
