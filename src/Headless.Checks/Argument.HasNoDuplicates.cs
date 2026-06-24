// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

// ReSharper disable PossibleMultipleEnumeration
public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> contains duplicate items.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if it contains no duplicate items.</returns>
    /// <remarks>Equality is evaluated with <see cref="EqualityComparer{T}.Default"/>. The sequence is enumerated once; pass a materialized or replayable sequence.</remarks>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> is null.</exception>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> contains duplicate items.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<T> HasNoDuplicates<T>(
        [JetBrainsNoEnumeration] [SystemNotNull] IEnumerable<T>? argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        return HasNoDuplicates(argument, comparer: null, message, paramName);
    }

    /// <summary>Throws an <see cref="ArgumentException" /> if <paramref name="argument" /> contains duplicate items.</summary>
    /// <param name="argument">The argument to check.</param>
    /// <param name="comparer">The comparer used to detect duplicates, or <see langword="null"/> for <see cref="EqualityComparer{T}.Default"/>.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if it contains no duplicate items.</returns>
    /// <remarks>The sequence is enumerated once; pass a materialized or replayable sequence.</remarks>
    /// <exception cref="ArgumentNullException">if <paramref name="argument" /> is null.</exception>
    /// <exception cref="ArgumentException">if <paramref name="argument" /> contains duplicate items.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<T> HasNoDuplicates<T>(
        [JetBrainsNoEnumeration] [SystemNotNull] IEnumerable<T>? argument,
        IEqualityComparer<T>? comparer,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
    {
        IsNotNull(argument, message, paramName);

        var seen = argument.TryGetNonEnumeratedCount(out var count)
            ? new HashSet<T>(count, comparer)
            : new HashSet<T>(comparer);

        foreach (var item in argument)
        {
            if (!seen.Add(item))
            {
                _ThrowForHasNoDuplicates(item, message, paramName);
            }
        }

        return argument;
    }

    [DoesNotReturn]
    private static void _ThrowForHasNoDuplicates<T>(T item, string? message, string? paramName)
    {
        throw new ArgumentException(
            message
                ?? $"The argument {paramName.ToAssertString()} must not contain duplicate items (Duplicate {item.ToAssertString()}).",
            paramName
        );
    }
}
