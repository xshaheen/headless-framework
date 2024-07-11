using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using NoEnumeration = JetBrains.Annotations.NoEnumerationAttribute;

namespace Framework.Arguments;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> is null or empty.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the value is not null or empty.</returns>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is null or empty.</exception>
    [DebuggerStepThrough]
    public static IReadOnlyCollection<T> IsNotNullOrEmpty<T>(
        [NotNull] IReadOnlyCollection<T>? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);
        IsNotEmpty(argument, message, paramName);

        return argument;
    }

    /// <inheritdoc cref="IsNotNullOrEmpty{T}(System.Collections.Generic.IEnumerable{T}?,string?,string?)"/>
    [DebuggerStepThrough]
    public static IEnumerable<T> IsNotNullOrEmpty<T>(
        [NoEnumeration] [NotNull] IEnumerable<T>? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);
        IsNotEmpty(argument, message, paramName);

        return argument;
    }

    /// <inheritdoc cref="IsNotNullOrEmpty{T}(System.Collections.Generic.IEnumerable{T}?,string?,string?)"/>
    [DebuggerStepThrough]
    public static string IsNotNullOrEmpty(
        [NotNull] string? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);
        IsNotEmpty(argument, message, paramName);

        return argument;
    }
}
