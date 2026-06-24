// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

// ReSharper disable PossibleMultipleEnumeration
public static partial class Argument
{
    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null, or an
    /// <see cref="ArgumentOutOfRangeException" /> if it does not contain exactly <paramref name="count"/> items.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="count">The required number of items.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if it contains exactly <paramref name="count"/> items.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> does not contain exactly <paramref name="count"/> items.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [OverloadResolutionPriority(1)]
    public static IReadOnlyCollection<T> HasCount<T>(
        [SystemNotNull] IReadOnlyCollection<T>? argument,
        int count,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);

        return argument.Count == count
            ? argument
            : throw new ArgumentOutOfRangeException(
                paramName,
                message
                    ?? $"The argument {paramName.ToAssertString()} must contain exactly {count} item(s) (Actual count {argument.Count})."
            );
    }

    /// <inheritdoc cref="HasCount{T}(IReadOnlyCollection{T}?,int,string?,string?)"/>
    /// <remarks>The sequence is enumerated once to count its items; pass a materialized or replayable sequence.</remarks>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<T> HasCount<T>(
        [JetBrainsNoEnumeration] [SystemNotNull] IEnumerable<T>? argument,
        int count,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);

        var actual = _Count(argument);

        return actual == count
            ? argument
            : throw new ArgumentOutOfRangeException(
                paramName,
                message
                    ?? $"The argument {paramName.ToAssertString()} must contain exactly {count} item(s) (Actual count {actual})."
            );
    }

    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null, or an
    /// <see cref="ArgumentOutOfRangeException" /> if it contains fewer than <paramref name="minCount"/> items.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="minCount">The minimum number of items (inclusive).</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if it contains at least <paramref name="minCount"/> items.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> contains fewer than <paramref name="minCount"/> items.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [OverloadResolutionPriority(1)]
    public static IReadOnlyCollection<T> HasMinCount<T>(
        [SystemNotNull] IReadOnlyCollection<T>? argument,
        int minCount,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);

        return argument.Count >= minCount
            ? argument
            : throw new ArgumentOutOfRangeException(
                paramName,
                message
                    ?? $"The argument {paramName.ToAssertString()} must contain at least {minCount} item(s) (Actual count {argument.Count})."
            );
    }

    /// <inheritdoc cref="HasMinCount{T}(IReadOnlyCollection{T}?,int,string?,string?)"/>
    /// <remarks>The sequence is enumerated once to count its items; pass a materialized or replayable sequence.</remarks>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<T> HasMinCount<T>(
        [JetBrainsNoEnumeration] [SystemNotNull] IEnumerable<T>? argument,
        int minCount,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);

        var actual = _Count(argument);

        return actual >= minCount
            ? argument
            : throw new ArgumentOutOfRangeException(
                paramName,
                message
                    ?? $"The argument {paramName.ToAssertString()} must contain at least {minCount} item(s) (Actual count {actual})."
            );
    }

    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null, or an
    /// <see cref="ArgumentOutOfRangeException" /> if it contains more than <paramref name="maxCount"/> items.
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="maxCount">The maximum number of items (inclusive).</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if it contains at most <paramref name="maxCount"/> items.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="argument" /> contains more than <paramref name="maxCount"/> items.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [OverloadResolutionPriority(1)]
    public static IReadOnlyCollection<T> HasMaxCount<T>(
        [SystemNotNull] IReadOnlyCollection<T>? argument,
        int maxCount,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);

        return argument.Count <= maxCount
            ? argument
            : throw new ArgumentOutOfRangeException(
                paramName,
                message
                    ?? $"The argument {paramName.ToAssertString()} must contain at most {maxCount} item(s) (Actual count {argument.Count})."
            );
    }

    /// <inheritdoc cref="HasMaxCount{T}(IReadOnlyCollection{T}?,int,string?,string?)"/>
    /// <remarks>The sequence is enumerated once to count its items; pass a materialized or replayable sequence.</remarks>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<T> HasMaxCount<T>(
        [JetBrainsNoEnumeration] [SystemNotNull] IEnumerable<T>? argument,
        int maxCount,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);

        var actual = _Count(argument);

        return actual <= maxCount
            ? argument
            : throw new ArgumentOutOfRangeException(
                paramName,
                message
                    ?? $"The argument {paramName.ToAssertString()} must contain at most {maxCount} item(s) (Actual count {actual})."
            );
    }

    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="argument" /> is null, or an
    /// <see cref="ArgumentOutOfRangeException" /> if its item count is outside the inclusive range
    /// [<paramref name="minCount"/>, <paramref name="maxCount"/>].
    /// </summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="minCount">The minimum number of items (inclusive).</param>
    /// <param name="maxCount">The maximum number of items (inclusive).</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if its item count is within range.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> is null.</exception>
    /// <exception cref="ArgumentException">if <paramref name="minCount"/> is greater than <paramref name="maxCount"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if the item count of <paramref name="argument" /> is out of range.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [OverloadResolutionPriority(1)]
    public static IReadOnlyCollection<T> HasCountBetween<T>(
        [SystemNotNull] IReadOnlyCollection<T>? argument,
        int minCount,
        int maxCount,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);
        Range(minCount, maxCount);

        return argument.Count >= minCount && argument.Count <= maxCount
            ? argument
            : throw new ArgumentOutOfRangeException(
                paramName,
                message
                    ?? $"The argument {paramName.ToAssertString()} must contain between {minCount} and {maxCount} item(s) (Actual count {argument.Count})."
            );
    }

    /// <inheritdoc cref="HasCountBetween{T}(IReadOnlyCollection{T}?,int,int,string?,string?)"/>
    /// <remarks>The sequence is enumerated once to count its items; pass a materialized or replayable sequence.</remarks>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<T> HasCountBetween<T>(
        [JetBrainsNoEnumeration] [SystemNotNull] IEnumerable<T>? argument,
        int minCount,
        int maxCount,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);
        Range(minCount, maxCount);

        var actual = _Count(argument);

        return actual >= minCount && actual <= maxCount
            ? argument
            : throw new ArgumentOutOfRangeException(
                paramName,
                message
                    ?? $"The argument {paramName.ToAssertString()} must contain between {minCount} and {maxCount} item(s) (Actual count {actual})."
            );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int _Count<T>(IEnumerable<T> source)
    {
        return source.TryGetNonEnumeratedCount(out var count) ? count : source.Count();
    }
}
