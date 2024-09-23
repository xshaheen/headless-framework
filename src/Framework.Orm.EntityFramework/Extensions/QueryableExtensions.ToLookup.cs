// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

[PublicAPI]
public static partial class QueryableExtensions
{
    public static async Task<HashSet<TSource>> ToHashSetAsync<TSource>(
        this IQueryable<TSource> source,
        CancellationToken cancellationToken = default
    )
    {
        var set = new HashSet<TSource>();

        await foreach (var element in source.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            set.Add(element);
        }

        return set;
    }

    public static async Task<ILookup<TKey, TSelected>> ToLookupAsync<TSource, TKey, TSelected>(
        this IQueryable<TSource> source,
        Func<TSource, TKey> keySelector,
        Func<TSource, TSelected> elementSelector,
        IEqualityComparer<TKey>? comparer = null,
        CancellationToken cancellationToken = default
    )
    {
        var list = await source.ToListAsync(cancellationToken);

        return list.ToLookup(keySelector, elementSelector, comparer);
    }
}
