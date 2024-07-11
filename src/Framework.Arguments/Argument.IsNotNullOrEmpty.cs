using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IReadOnlyCollection<T> IsNotNullOrEmpty<T>(
        [SystemNotNull] IReadOnlyCollection<T>? argument,
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<T> IsNotNullOrEmpty<T>(
        [JetBrainsNoEnumeration] [SystemNotNull] IEnumerable<T>? argument,
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string IsNotNullOrEmpty(
        [SystemNotNull] string? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);
        IsNotEmpty(argument, message, paramName);

        return argument;
    }
}
