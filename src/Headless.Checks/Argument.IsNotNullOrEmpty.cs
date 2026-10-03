// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null,
    /// or an <see cref="ArgumentException" /> if <paramref name="argument" /> is empty.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">A custom error message, or <see langword="null"/> to use the default error message.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <returns><paramref name="argument" /> when the value is neither null nor empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="argument" /> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="argument" /> is empty.</exception>
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

    /// <inheritdoc cref="IsNotNullOrEmpty{T}(System.Collections.Generic.IReadOnlyCollection{T}?,string?,string?)"/>
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

    /// <inheritdoc cref="IsNotNullOrEmpty{T}(System.Collections.Generic.IReadOnlyCollection{T}?,string?,string?)"/>
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
