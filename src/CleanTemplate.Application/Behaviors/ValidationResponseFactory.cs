using System.Reflection;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;

namespace CleanTemplate.Application.Behaviors;

internal static class ValidationResponseFactory
{
    internal static bool IsSupported(Type type) => type == typeof(Result) ||
        (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>));

    internal static Func<IEnumerable<Error>, TResponse> Create<TResponse>()
    {
        var type = typeof(TResponse);
        if (!IsSupported(type))
            throw new InvalidOperationException($"Validated requests require response type Result or Result<T>; received {type.FullName}.");

        if (type == typeof(Result))
            return errors => (TResponse)(object)Result.Failure(errors);

        var method = typeof(ValidationResponseFactory).GetMethod(nameof(CreateGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type.GenericTypeArguments[0]);
        return method.CreateDelegate<Func<IEnumerable<Error>, TResponse>>();
    }

    private static Result<T> CreateGeneric<T>(IEnumerable<Error> errors) => Result<T>.Failure(errors);
}
