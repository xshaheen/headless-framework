// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Framework.Kernel.Checks.Internals;

namespace Framework.Kernel.Checks;

// ReSharper disable PossibleMultipleEnumeration
public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> is empty.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="paramName" /> if the value is not empty.</returns>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is empty.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NotNullIfNotNull(nameof(argument))]
    public static IReadOnlyCollection<T>? IsNotEmpty<T>(
        IReadOnlyCollection<T>? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument is { Count: 0 }
            ? throw new ArgumentException(
                message ?? $"Required argument {paramName.ToAssertString()} was empty.",
                paramName
            )
            : argument;
    }

    /// <inheritdoc cref="IsNotEmpty{T}(IReadOnlyCollection{T}?,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NotNullIfNotNull(nameof(argument))]
    public static IEnumerable<T>? IsNotEmpty<T>(
        [JetBrainsNoEnumeration] IEnumerable<T>? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (argument is null)
        {
            return argument;
        }

        if (!argument.Any())
        {
            throw new ArgumentException(
                message ?? $"Required argument {paramName.ToAssertString()} was empty.",
                paramName
            );
        }

        return argument;
    }

    /// <inheritdoc cref="IsNotEmpty{T}(IReadOnlyCollection{T}?,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NotNullIfNotNull(nameof(argument))]
    public static string? IsNotEmpty(
        string? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return argument is { Length: 0 }
            ? throw new ArgumentException(
                message ?? $"Required argument {paramName.ToAssertString()} was empty.",
                paramName
            )
            : argument;
    }
}
