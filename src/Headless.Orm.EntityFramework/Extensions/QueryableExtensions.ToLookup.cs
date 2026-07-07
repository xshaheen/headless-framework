// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Headless.EntityFramework;

[PublicAPI]
public static partial class QueryableExtensions
{
    /// <summary>
    /// Asynchronously materializes the query and groups the results into an <see cref="ILookup{TKey,TElement}"/>.
    /// </summary>
    /// <typeparam name="TSource">The source element type.</typeparam>
    /// <typeparam name="TKey">The lookup key type.</typeparam>
    /// <typeparam name="TSelected">The projected element type.</typeparam>
    /// <param name="source">The source queryable.</param>
    /// <param name="keySelector">A function to extract the lookup key from each element.</param>
    /// <param name="elementSelector">A function to project each element into the lookup value.</param>
    /// <param name="comparer">Optional key equality comparer.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>An <see cref="ILookup{TKey,TElement}"/> built from the query results.</returns>
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
