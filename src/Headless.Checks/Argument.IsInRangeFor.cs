// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="index"/> is not a valid index into a
    /// collection of <paramref name="count"/> items (that is, it is negative or not less than <paramref name="count"/>).
    /// </summary>
    /// <param name="index">The index to check.</param>
    /// <param name="count">The number of items the index addresses into.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="index" /> if it is a valid index.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="index" /> is negative or not less than <paramref name="count"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IsInRangeFor(
        int index,
        int count,
        string? message = null,
        [CallerArgumentExpression(nameof(index))] string? paramName = null
    )
    {
        return (uint)index < (uint)count ? index : _ThrowIndexOutOfRangeFor(index, count, message, paramName);
    }

    /// <summary>
    /// Throws an <see cref="ArgumentNullException" /> if <paramref name="collection"/> is null, or an
    /// <see cref="ArgumentOutOfRangeException" /> if <paramref name="index"/> is not a valid index into it.
    /// </summary>
    /// <param name="index">The index to check.</param>
    /// <param name="collection">The collection the index addresses into.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="index" /> if it is a valid index into <paramref name="collection"/>.</returns>
    /// <exception cref="ArgumentNullException">if <paramref name="collection" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="index" /> is not a valid index into <paramref name="collection"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IsInRangeFor<T>(
        int index,
        IReadOnlyCollection<T> collection,
        string? message = null,
        [CallerArgumentExpression(nameof(index))] string? paramName = null
    )
    {
        IsNotNull(collection);

        return (uint)index < (uint)collection.Count
            ? index
            : _ThrowIndexOutOfRangeFor(index, collection.Count, message, paramName);
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="index"/> is not a valid index into
    /// <paramref name="span"/>.
    /// </summary>
    /// <param name="index">The index to check.</param>
    /// <param name="span">The span the index addresses into.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="index" /> if it is a valid index into <paramref name="span"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">if <paramref name="index" /> is not a valid index into <paramref name="span"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IsInRangeFor<T>(
        int index,
        ReadOnlySpan<T> span,
        string? message = null,
        [CallerArgumentExpression(nameof(index))] string? paramName = null
    )
    {
        return (uint)index < (uint)span.Length
            ? index
            : _ThrowIndexOutOfRangeFor(index, span.Length, message, paramName);
    }

    [DoesNotReturn]
    private static int _ThrowIndexOutOfRangeFor(int index, int count, string? message, string? paramName)
    {
        throw new ArgumentOutOfRangeException(
            paramName,
            message
                ?? string.Create(
                    CultureInfo.InvariantCulture,
                    $"The argument {paramName.ToAssertString()} = {index} must be a valid index for a collection of {count} item(s) (Valid range [0, {count - 1}])."
                )
        );
    }
}
