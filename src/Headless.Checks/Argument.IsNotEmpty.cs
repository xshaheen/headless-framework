// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

// ReSharper disable PossibleMultipleEnumeration
public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> is empty.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the value is not empty.</returns>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is empty.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [OverloadResolutionPriority(1)]
    public static ReadOnlySpan<T> IsNotEmpty<T>(
        ReadOnlySpan<T> argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (argument.IsEmpty)
        {
            _ThrowForIsNotEmpty(message, paramName);
        }

        return argument;
    }

    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> is empty.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the value is not empty.</returns>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is empty.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<T> IsNotEmpty<T>(
        Span<T> argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (argument.IsEmpty)
        {
            _ThrowForIsNotEmpty(message, paramName);
        }

        return argument;
    }

    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> is empty.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the value is not empty.</returns>
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
        if (argument is { Count: 0 })
        {
            _ThrowForIsNotEmpty(message, paramName);
        }

        return argument;
    }

    /// <inheritdoc cref="IsNotEmpty{T}(IReadOnlyCollection{T}?,string?,string?)"/>
    /// <remarks>The sequence is enumerated once (via <see cref="System.Linq.Enumerable.Any{T}(IEnumerable{T})"/>) to test for emptiness. Pass a materialized or replayable sequence; a once-only or side-effecting iterator will be partially consumed.</remarks>
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
            _ThrowForIsNotEmpty(message, paramName);
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
        if (argument is { Length: 0 })
        {
            _ThrowForIsNotEmpty(message, paramName);
        }

        return argument;
    }

    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> is <see cref="Guid.Empty"/>.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the value is not <see cref="Guid.Empty"/>.</returns>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is <see cref="Guid.Empty"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid IsNotEmpty(
        Guid argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (argument == Guid.Empty)
        {
            _ThrowForIsNotEmptyGuid(message, paramName);
        }

        return argument;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> has a value that is <see cref="Guid.Empty"/>.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the value is null or not <see cref="Guid.Empty"/>.</returns>
    /// <remarks>A <see langword="null"/> value is accepted and returned as-is; only a present <see cref="Guid.Empty"/> value throws.</remarks>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> has a value that is <see cref="Guid.Empty"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NotNullIfNotNull(nameof(argument))]
    public static Guid? IsNotEmpty(
        Guid? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (argument == Guid.Empty)
        {
            _ThrowForIsNotEmptyGuid(message, paramName);
        }

        return argument;
    }

    [DoesNotReturn]
    private static void _ThrowForIsNotEmpty(string? message, string? paramName)
    {
        throw new ArgumentException(message ?? $"Required argument {paramName.ToAssertString()} was empty.", paramName);
    }

    [DoesNotReturn]
    private static void _ThrowForIsNotEmptyGuid(string? message, string? paramName)
    {
        throw new ArgumentException(
            message ?? $"Required argument {paramName.ToAssertString()} was an empty GUID.",
            paramName
        );
    }
}
