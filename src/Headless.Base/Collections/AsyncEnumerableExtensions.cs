// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
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
    public static async IAsyncEnumerable<T> ConcatAsync<T>(
        this IAsyncEnumerable<T> first,
        IAsyncEnumerable<T> second,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var item in first.WithCancellation(cancellationToken).AnyContext())
        {
            yield return item;
        }

        await foreach (var item in second.WithCancellation(cancellationToken).AnyContext())
        {
            yield return item;
        }
    }

    public static IAsyncEnumerable<T> DistinctAsync<T>(
        this IAsyncEnumerable<T> enumerable,
        CancellationToken cancellationToken = default
    )
    {
        return DistinctAsync(enumerable, comparer: null, cancellationToken);
    }

    public static async IAsyncEnumerable<T> DistinctAsync<T>(
        this IAsyncEnumerable<T> enumerable,
        IEqualityComparer<T>? comparer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var hashSet = new HashSet<T>(comparer);

        await foreach (var item in enumerable.WithCancellation(cancellationToken).AnyContext())
        {
            if (hashSet.Add(item))
            {
                yield return item;
            }
        }
    }

    public static IAsyncEnumerable<T> DistinctByAsync<T, TKey>(
        this IAsyncEnumerable<T> enumerable,
        Func<T, TKey> getKey,
        CancellationToken cancellationToken = default
    )
    {
        return DistinctByAsync(enumerable, getKey, comparer: null, cancellationToken);
    }

    public static async IAsyncEnumerable<T> DistinctByAsync<T, TKey>(
        this IAsyncEnumerable<T> enumerable,
        Func<T, TKey> getKey,
        IEqualityComparer<TKey>? comparer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var hashSet = new HashSet<TKey>(comparer);

        await foreach (var item in enumerable.WithCancellation(cancellationToken).AnyContext())
        {
            var key = getKey(item);

            if (hashSet.Add(key))
            {
                yield return item;
            }
        }
    }

    public static async IAsyncEnumerable<TResult> OfTypeAsync<T, TResult>(
        this IAsyncEnumerable<T> enumerable,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).AnyContext())
        {
            if (item is TResult result)
            {
                yield return result;
            }
        }
    }

    public static async IAsyncEnumerable<TResult> SelectAsync<T, TResult>(
        this IAsyncEnumerable<T> enumerable,
        Func<T, TResult> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).AnyContext())
        {
            yield return selector(item);
        }
    }

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

        await foreach (var item in enumerable.WithCancellation(cancellationToken).AnyContext())
        {
            yield return item;

            if (--count == 0)
            {
                yield break;
            }
        }
    }

    public static async IAsyncEnumerable<T> TakeWhileAsync<T>(
        this IAsyncEnumerable<T> enumerable,
        Func<T, bool> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).AnyContext())
        {
            if (!selector(item))
            {
                yield break;
            }

            yield return item;
        }
    }

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
                while (count > 0 && await enumerator.MoveNextAsync().AnyContext())
                {
                    count--;
                }

                while (await enumerator.MoveNextAsync().AnyContext())
                {
                    yield return enumerator.Current;
                }
            }
        }
        finally
        {
            if (enumerator is not null)
            {
                await enumerator.DisposeAsync().AnyContext();
            }
        }
    }

    public static async IAsyncEnumerable<T> SkipWhileAsync<T>(
        this IAsyncEnumerable<T> enumerable,
        Func<T, bool> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).AnyContext())
        {
            if (selector(item))
            {
                continue;
            }

            yield return item;
        }
    }

    public static async IAsyncEnumerable<T> WhereAsync<T>(
        this IAsyncEnumerable<T> enumerable,
        Func<T, bool> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).AnyContext())
        {
            if (selector(item))
            {
                yield return item;
            }
        }
    }

    public static IAsyncEnumerable<T> WhereNotNull<T>(
        this IAsyncEnumerable<T?> enumerable,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        return enumerable.WhereAsync(item => item is not null, cancellationToken)!;
    }

    public static IAsyncEnumerable<string> WhereNotNullOrEmpty(
        this IAsyncEnumerable<string?> source,
        CancellationToken cancellationToken = default
    )
    {
        return source.WhereAsync(item => !string.IsNullOrEmpty(item), cancellationToken)!;
    }

    public static IAsyncEnumerable<string> WhereNotNullOrWhiteSpace(
        this IAsyncEnumerable<string?> source,
        CancellationToken cancellationToken = default
    )
    {
        return source.WhereAsync(item => !string.IsNullOrWhiteSpace(item), cancellationToken)!;
    }
}
