// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

[PublicAPI]
public static class AsyncEnumerableExtensions
{
    extension<T>(IAsyncEnumerable<T> first)
    {
        public async IAsyncEnumerable<T> ConcatAsync(
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

        public IAsyncEnumerable<T> DistinctAsync(CancellationToken cancellationToken = default)
        {
            return first.DistinctAsync(comparer: null, cancellationToken);
        }

        public async IAsyncEnumerable<T> DistinctAsync(
            IEqualityComparer<T>? comparer,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            var hashSet = new HashSet<T>(comparer);

            await foreach (var item in first.WithCancellation(cancellationToken).AnyContext())
            {
                if (hashSet.Add(item))
                {
                    yield return item;
                }
            }
        }

        public IAsyncEnumerable<T> DistinctByAsync<TKey>(
            Func<T, TKey> getKey,
            CancellationToken cancellationToken = default
        )
        {
            return first.DistinctByAsync(getKey, comparer: null, cancellationToken);
        }

        public async IAsyncEnumerable<T> DistinctByAsync<TKey>(
            Func<T, TKey> getKey,
            IEqualityComparer<TKey>? comparer,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            var hashSet = new HashSet<TKey>(comparer);

            await foreach (var item in first.WithCancellation(cancellationToken).AnyContext())
            {
                var key = getKey(item);

                if (hashSet.Add(key))
                {
                    yield return item;
                }
            }
        }

        public async IAsyncEnumerable<TResult> OfTypeAsync<TResult>(
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await foreach (var item in first.WithCancellation(cancellationToken).AnyContext())
            {
                if (item is TResult result)
                {
                    yield return result;
                }
            }
        }

        public async IAsyncEnumerable<TResult> SelectAsync<TResult>(
            Func<T, TResult> selector,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await foreach (var item in first.WithCancellation(cancellationToken).AnyContext())
            {
                yield return selector(item);
            }
        }

        public async IAsyncEnumerable<T> TakeAsync(
            int count,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            if (count <= 0)
            {
                yield break;
            }

            await foreach (var item in first.WithCancellation(cancellationToken).AnyContext())
            {
                yield return item;

                if (--count == 0)
                {
                    yield break;
                }
            }
        }

        public async IAsyncEnumerable<T> TakeWhileAsync(
            Func<T, bool> selector,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await foreach (var item in first.WithCancellation(cancellationToken).AnyContext())
            {
                if (!selector(item))
                {
                    yield break;
                }

                yield return item;
            }
        }

        public async IAsyncEnumerable<T> SkipAsync(
            int count,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            IAsyncEnumerator<T>? enumerator = null;

            try
            {
                enumerator = first.GetAsyncEnumerator(cancellationToken);

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

        public async IAsyncEnumerable<T> SkipWhileAsync(
            Func<T, bool> selector,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await foreach (var item in first.WithCancellation(cancellationToken).AnyContext())
            {
                if (selector(item))
                {
                    continue;
                }

                yield return item;
            }
        }

        public async IAsyncEnumerable<T> WhereAsync(
            Func<T, bool> selector,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await foreach (var item in first.WithCancellation(cancellationToken).AnyContext())
            {
                if (selector(item))
                {
                    yield return item;
                }
            }
        }
    }

    extension(IAsyncEnumerable<string?> source)
    {
        public IAsyncEnumerable<string> WhereNotNullOrEmpty(CancellationToken cancellationToken = default)
        {
            return source.WhereAsync(item => !string.IsNullOrEmpty(item), cancellationToken)!;
        }

        public IAsyncEnumerable<string> WhereNotNullOrWhiteSpace(CancellationToken cancellationToken = default)
        {
            return source.WhereAsync(item => !string.IsNullOrWhiteSpace(item), cancellationToken)!;
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
}
