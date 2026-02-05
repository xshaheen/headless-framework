// Copyright (c) Mahmoud Shaheen. All rights reserved.

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

[PublicAPI]
public static partial class QueryableExtensions
{
    public static async Task<ILookup<TKey, TSelected>> ToLookupAsync<TSource, TKey, TSelected>(
        this IQueryable<TSource> source,
        Func<TSource, TKey> keySelector,
        Func<TSource, TSelected> elementSelector,
        IEqualityComparer<TKey>? comparer = null,
        CancellationToken cancellationToken = default
    )
    {
        var list = await source.ToListAsync(cancellationToken).ConfigureAwait(false);

        return list.ToLookup(keySelector, elementSelector, comparer);
    }
}
