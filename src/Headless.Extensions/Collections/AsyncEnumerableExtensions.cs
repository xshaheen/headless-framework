// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

/// <summary>
/// Extension methods for <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
/// <remarks>
/// Methods like ToListAsync, AnyAsync, CountAsync, FirstAsync, LastAsync, ContainsAsync, LongCountAsync
/// are now provided by .NET's System.Linq.AsyncEnumerable and have been removed from this class.
/// </remarks>
[PublicAPI]
public static class AsyncEnumerableExtensions
{
    /// <summary>Concatenates two async sequences, yielding all elements of <paramref name="first"/> followed by all elements of <paramref name="second"/>.</summary>
    /// <typeparam name="T">The type of the elements of the sequences.</typeparam>
    /// <param name="first">The first async sequence to enumerate.</param>
    /// <param name="second">The async sequence to yield after <paramref name="first"/> is exhausted.</param>
    /// <param name="cancellationToken">A token that flows to both source sequences and stops enumeration when cancelled.</param>
    /// <returns>An async sequence containing the elements of <paramref name="first"/> followed by those of <paramref name="second"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static async IAsyncEnumerable<T> ConcatAsync<T>(
        this IAsyncEnumerable<T> first,
        IAsyncEnumerable<T> second,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var item in first.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }

        await foreach (var item in second.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <summary>Yields the distinct elements of an async sequence using the default equality comparer.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="enumerable"/>.</typeparam>
    /// <param name="enumerable">The async sequence to remove duplicates from.</param>
    /// <param name="cancellationToken">A token that stops enumeration when cancelled.</param>
    /// <returns>An async sequence that contains the distinct elements of <paramref name="enumerable"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static IAsyncEnumerable<T> DistinctAsync<T>(
        this IAsyncEnumerable<T> enumerable,
        CancellationToken cancellationToken = default
    )
    {
        return DistinctAsync(enumerable, comparer: null, cancellationToken);
    }

    /// <summary>Yields the distinct elements of an async sequence using the specified equality comparer.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="enumerable"/>.</typeparam>
    /// <param name="enumerable">The async sequence to remove duplicates from.</param>
    /// <param name="comparer">The comparer used to compare elements, or <see langword="null"/> to use the default comparer.</param>
    /// <param name="cancellationToken">A token that stops enumeration when cancelled.</param>
    /// <returns>An async sequence that contains the distinct elements of <paramref name="enumerable"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static async IAsyncEnumerable<T> DistinctAsync<T>(
        this IAsyncEnumerable<T> enumerable,
        IEqualityComparer<T>? comparer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var hashSet = new HashSet<T>(comparer);

        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (hashSet.Add(item))
            {
                yield return item;
            }
        }
    }

    /// <summary>Yields the elements of an async sequence with distinct keys, using the default key comparer.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="enumerable"/>.</typeparam>
    /// <typeparam name="TKey">The type of the key projected by <paramref name="getKey"/>.</typeparam>
    /// <param name="enumerable">The async sequence to remove duplicates from.</param>
    /// <param name="getKey">A function that projects each element to the key used for distinctness.</param>
    /// <param name="cancellationToken">A token that stops enumeration when cancelled.</param>
    /// <returns>An async sequence that contains the elements of <paramref name="enumerable"/> with distinct keys.</returns>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static IAsyncEnumerable<T> DistinctByAsync<T, TKey>(
        this IAsyncEnumerable<T> enumerable,
        Func<T, TKey> getKey,
        CancellationToken cancellationToken = default
    )
    {
        return DistinctByAsync(enumerable, getKey, comparer: null, cancellationToken);
    }

    /// <summary>Yields the elements of an async sequence with distinct keys, using the specified key comparer.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="enumerable"/>.</typeparam>
    /// <typeparam name="TKey">The type of the key projected by <paramref name="getKey"/>.</typeparam>
    /// <param name="enumerable">The async sequence to remove duplicates from.</param>
    /// <param name="getKey">A function that projects each element to the key used for distinctness.</param>
    /// <param name="comparer">The comparer used to compare keys, or <see langword="null"/> to use the default comparer.</param>
    /// <param name="cancellationToken">A token that stops enumeration when cancelled.</param>
    /// <returns>An async sequence that contains the elements of <paramref name="enumerable"/> with distinct keys.</returns>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static async IAsyncEnumerable<T> DistinctByAsync<T, TKey>(
        this IAsyncEnumerable<T> enumerable,
        Func<T, TKey> getKey,
        IEqualityComparer<TKey>? comparer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var hashSet = new HashSet<TKey>(comparer);

        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var key = getKey(item);

            if (hashSet.Add(key))
            {
                yield return item;
            }
        }
    }

    /// <summary>Filters an async sequence to the elements that can be cast to <typeparamref name="TResult"/>.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="enumerable"/>.</typeparam>
    /// <typeparam name="TResult">The type to filter the elements to.</typeparam>
    /// <param name="enumerable">The async sequence to filter.</param>
    /// <param name="cancellationToken">A token that stops enumeration when cancelled.</param>
    /// <returns>An async sequence that contains the elements of type <typeparamref name="TResult"/> from <paramref name="enumerable"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static async IAsyncEnumerable<TResult> OfTypeAsync<T, TResult>(
        this IAsyncEnumerable<T> enumerable,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (item is TResult result)
            {
                yield return result;
            }
        }
    }

    /// <summary>Projects each element of an async sequence into a new form.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="enumerable"/>.</typeparam>
    /// <typeparam name="TResult">The type of the value returned by <paramref name="selector"/>.</typeparam>
    /// <param name="enumerable">The async sequence to project.</param>
    /// <param name="selector">A transform function applied to each element.</param>
    /// <param name="cancellationToken">A token that stops enumeration when cancelled.</param>
    /// <returns>An async sequence whose elements are the result of applying <paramref name="selector"/> to each element of <paramref name="enumerable"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static async IAsyncEnumerable<TResult> SelectAsync<T, TResult>(
        this IAsyncEnumerable<T> enumerable,
        Func<T, TResult> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return selector(item);
        }
    }

    /// <summary>Yields the first <paramref name="count"/> elements of an async sequence.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="enumerable"/>.</typeparam>
    /// <param name="enumerable">The async sequence to take elements from.</param>
    /// <param name="count">The number of elements to take. Values less than or equal to zero yield an empty sequence.</param>
    /// <param name="cancellationToken">A token that stops enumeration when cancelled.</param>
    /// <returns>An async sequence that contains at most the first <paramref name="count"/> elements of <paramref name="enumerable"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static async IAsyncEnumerable<T> TakeAsync<T>(
        this IAsyncEnumerable<T> enumerable,
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        if (count <= 0)
        {
            yield break;
        }

        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;

            if (--count == 0)
            {
                yield break;
            }
        }
    }

    /// <summary>Yields elements from an async sequence as long as <paramref name="selector"/> returns <see langword="true"/>, then stops.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="enumerable"/>.</typeparam>
    /// <param name="enumerable">The async sequence to take elements from.</param>
    /// <param name="selector">A predicate evaluated for each element; enumeration stops at the first element for which it returns <see langword="false"/>.</param>
    /// <param name="cancellationToken">A token that stops enumeration when cancelled.</param>
    /// <returns>An async sequence that contains the leading elements of <paramref name="enumerable"/> that satisfy <paramref name="selector"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static async IAsyncEnumerable<T> TakeWhileAsync<T>(
        this IAsyncEnumerable<T> enumerable,
        Func<T, bool> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!selector(item))
            {
                yield break;
            }

            yield return item;
        }
    }

    /// <summary>Bypasses the first <paramref name="count"/> elements of an async sequence and yields the remaining elements.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="enumerable"/>.</typeparam>
    /// <param name="enumerable">The async sequence to skip elements from.</param>
    /// <param name="count">The number of elements to skip. Values less than or equal to zero yield an empty sequence.</param>
    /// <param name="cancellationToken">A token that stops enumeration when cancelled.</param>
    /// <returns>An async sequence that contains the elements of <paramref name="enumerable"/> that occur after the skipped elements.</returns>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static async IAsyncEnumerable<T> SkipAsync<T>(
        this IAsyncEnumerable<T> enumerable,
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        IAsyncEnumerator<T>? enumerator = null;

        try
        {
            enumerator = enumerable.GetAsyncEnumerator(cancellationToken);

            if (count > 0)
            {
                while (count > 0 && await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    count--;
                }

                while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    yield return enumerator.Current;
                }
            }
        }
        finally
        {
            if (enumerator is not null)
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Bypasses elements of an async sequence as long as <paramref name="selector"/> returns <see langword="true"/>, then yields the remaining elements.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="enumerable"/>.</typeparam>
    /// <param name="enumerable">The async sequence to skip elements from.</param>
    /// <param name="selector">A predicate evaluated for each element; skipping stops at the first element for which it returns <see langword="false"/>.</param>
    /// <param name="cancellationToken">A token that stops enumeration when cancelled.</param>
    /// <returns>An async sequence that contains the elements of <paramref name="enumerable"/> starting at the first element that does not satisfy <paramref name="selector"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static async IAsyncEnumerable<T> SkipWhileAsync<T>(
        this IAsyncEnumerable<T> enumerable,
        Func<T, bool> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (selector(item))
            {
                continue;
            }

            yield return item;
        }
    }

    /// <summary>Filters an async sequence to the elements that satisfy <paramref name="selector"/>.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="enumerable"/>.</typeparam>
    /// <param name="enumerable">The async sequence to filter.</param>
    /// <param name="selector">A predicate that each element is tested against.</param>
    /// <param name="cancellationToken">A token that stops enumeration when cancelled.</param>
    /// <returns>An async sequence that contains the elements of <paramref name="enumerable"/> that satisfy <paramref name="selector"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static async IAsyncEnumerable<T> WhereAsync<T>(
        this IAsyncEnumerable<T> enumerable,
        Func<T, bool> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (selector(item))
            {
                yield return item;
            }
        }
    }

    /// <summary>Filters an async sequence to its non-null elements.</summary>
    /// <typeparam name="T">The reference type of the elements of <paramref name="enumerable"/>.</typeparam>
    /// <param name="enumerable">The async sequence to filter.</param>
    /// <param name="cancellationToken">A token that stops enumeration when cancelled.</param>
    /// <returns>An async sequence that contains the non-null elements of <paramref name="enumerable"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static IAsyncEnumerable<T> WhereNotNull<T>(
        this IAsyncEnumerable<T?> enumerable,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        return enumerable.WhereAsync(item => item is not null, cancellationToken)!;
    }

    /// <summary>Filters an async sequence of strings to those that are neither <see langword="null"/> nor empty.</summary>
    /// <param name="source">The async sequence to filter.</param>
    /// <param name="cancellationToken">A token that stops enumeration when cancelled.</param>
    /// <returns>An async sequence that contains the non-null, non-empty strings of <paramref name="source"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static IAsyncEnumerable<string> WhereNotNullOrEmpty(
        this IAsyncEnumerable<string?> source,
        CancellationToken cancellationToken = default
    )
    {
        return source.WhereAsync(item => !string.IsNullOrEmpty(item), cancellationToken)!;
    }

    /// <summary>Filters an async sequence of strings to those that are neither <see langword="null"/>, empty, nor consist only of white-space characters.</summary>
    /// <param name="source">The async sequence to filter.</param>
    /// <param name="cancellationToken">A token that stops enumeration when cancelled.</param>
    /// <returns>An async sequence that contains the non-null, non-empty, non-white-space strings of <paramref name="source"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static IAsyncEnumerable<string> WhereNotNullOrWhiteSpace(
        this IAsyncEnumerable<string?> source,
        CancellationToken cancellationToken = default
    )
    {
        return source.WhereAsync(item => !string.IsNullOrWhiteSpace(item), cancellationToken)!;
    }
}
