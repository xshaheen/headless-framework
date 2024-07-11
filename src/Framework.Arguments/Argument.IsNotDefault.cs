using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Framework.Arguments;

public static partial class Argument
{
    /// <summary>Asserts that the input value is <see langword="default"/>.</summary>
    /// <typeparam name="T">The type of <see langword="struct"/> value type being tested.</typeparam>
    /// <param name="argument">The input value to test.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">The name of the input parameter being tested.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="argument"/> is not <see langword="default"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [DebuggerStepThrough]
    public static void IsDefault<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct
    {
        if (EqualityComparer<T>.Default.Equals(argument, default))
        {
            return;
        }

        throw new ArgumentException(
            message ?? $"The argument '{_AssertString(paramName)}' must be default.",
            paramName
        );
    }

    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> is default(T).</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the value is not default for that type.</returns>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is default for that type.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [DebuggerStepThrough]
    public static T IsNotDefault<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct
    {
        if (EqualityComparer<T>.Default.Equals(argument, default))
        {
            throw new ArgumentException(
                message ?? $"{_AssertString(paramName)} cannot be the default value of {typeof(T).Name}.",
                paramName
            );
        }

        return argument;
    }

    /// <inheritdoc cref="IsNotDefault{T}(T,string?,string?)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [DebuggerStepThrough]
    public static T? IsNotDefault<T>(
        T? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct
    {
        if (argument is null)
        {
            return argument;
        }

        if (EqualityComparer<T>.Default.Equals(argument.Value, default))
        {
            throw new ArgumentException(
                message ?? $"{_AssertString(paramName)} cannot be the default value of {typeof(T).Name}.",
                paramName
            );
        }

        return argument;
    }
}
