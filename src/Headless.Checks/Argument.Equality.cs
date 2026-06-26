// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> is not equal to <paramref name="expected"/>.</summary>
    /// <typeparam name="T">The type of values to compare.</typeparam>
    /// <param name="argument">The argument to check.</param>
    /// <param name="expected">The value the argument must be equal to.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if it is equal to <paramref name="expected"/>.</returns>
    /// <remarks>Equality is evaluated with <see cref="EqualityComparer{T}.Default"/>. Use the overload taking an <see cref="IEqualityComparer{T}"/> to supply custom semantics (for example case-insensitive strings).</remarks>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is not equal to <paramref name="expected"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsEqualTo<T>(
        T argument,
        T expected,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (!EqualityComparer<T>.Default.Equals(argument, expected))
        {
            _ThrowForIsEqualTo(expected, message, paramName);
        }

        return argument;
    }

    /// <inheritdoc cref="IsEqualTo{T}(T,T,string?,string?)"/>
    /// <param name="argument">The argument to check.</param>
    /// <param name="expected">The value the argument must be equal to.</param>
    /// <param name="comparer">The comparer used to evaluate equality.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsEqualTo<T>(
        T argument,
        T expected,
        IEqualityComparer<T> comparer,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(comparer);

        if (!comparer.Equals(argument, expected))
        {
            _ThrowForIsEqualTo(expected, message, paramName);
        }

        return argument;
    }

    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> is equal to <paramref name="other"/>.</summary>
    /// <typeparam name="T">The type of values to compare.</typeparam>
    /// <param name="argument">The argument to check.</param>
    /// <param name="other">The value the argument must not be equal to.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if it is not equal to <paramref name="other"/>.</returns>
    /// <remarks>Equality is evaluated with <see cref="EqualityComparer{T}.Default"/>. Use the overload taking an <see cref="IEqualityComparer{T}"/> to supply custom semantics (for example case-insensitive strings).</remarks>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> is equal to <paramref name="other"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNotEqualTo<T>(
        T argument,
        T other,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        if (EqualityComparer<T>.Default.Equals(argument, other))
        {
            _ThrowForIsNotEqualTo(other, message, paramName);
        }

        return argument;
    }

    /// <inheritdoc cref="IsNotEqualTo{T}(T,T,string?,string?)"/>
    /// <param name="argument">The argument to check.</param>
    /// <param name="other">The value the argument must not be equal to.</param>
    /// <param name="comparer">The comparer used to evaluate equality.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsNotEqualTo<T>(
        T argument,
        T other,
        IEqualityComparer<T> comparer,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(comparer);

        if (comparer.Equals(argument, other))
        {
            _ThrowForIsNotEqualTo(other, message, paramName);
        }

        return argument;
    }

    [DoesNotReturn]
    private static void _ThrowForIsEqualTo<T>(T expected, string? message, string? paramName)
    {
        throw new ArgumentException(
            message ?? $"The argument {paramName.ToAssertString()} must be equal to {expected.ToAssertString()}.",
            paramName
        );
    }

    [DoesNotReturn]
    private static void _ThrowForIsNotEqualTo<T>(T other, string? message, string? paramName)
    {
        throw new ArgumentException(
            message ?? $"The argument {paramName.ToAssertString()} must not be equal to {other.ToAssertString()}.",
            paramName
        );
    }
}
