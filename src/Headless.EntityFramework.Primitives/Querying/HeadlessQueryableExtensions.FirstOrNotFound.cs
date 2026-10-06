// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;

namespace Microsoft.EntityFrameworkCore;

public static partial class HeadlessQueryableExtensions
{
    /// <summary>
    /// Returns the first element of the query, or throws <c>EntityNotFoundException</c> naming
    /// <paramref name="entity"/> and <paramref name="key"/> when the query is empty.
    /// </summary>
    /// <remarks>
    /// Use it for projections and other queries that <c>FirstByIdAsync</c> cannot express, so the not-found failure
    /// keeps the same shape: <c>query.Where(x =&gt; x.Id == id).Select(x =&gt; new View { … }).FirstOrNotFoundAsync(nameof(Country), id, ct)</c>.
    /// The element type is a reference type because a default value type cannot be told apart from a match.
    /// </remarks>
    /// <typeparam name="T">The element type of the query, typically a projection.</typeparam>
    /// <typeparam name="TKey">The type of the key reported in the exception.</typeparam>
    /// <param name="source">The source queryable, already filtered to the wanted row.</param>
    /// <param name="entity">The entity name reported in the exception, typically <c>nameof(TEntity)</c>.</param>
    /// <param name="key">The key reported in the exception. Formatted with the invariant culture when formattable.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>The first element of the query.</returns>
    /// <exception cref="EntityNotFoundException">The query returns no element.</exception>
    public static async ValueTask<T> FirstOrNotFoundAsync<T, TKey>(
        this IQueryable<T> source,
        string entity,
        TKey key,
        CancellationToken cancellationToken = default
    )
        where T : class
        where TKey : notnull
    {
        var item = await source.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return item ?? throw new EntityNotFoundException(entity, _FormatKey(key));
    }

    private static string _FormatKey<TKey>(TKey key)
        where TKey : notnull
    {
        return key is IFormattable formattable
            ? formattable.ToString(format: null, CultureInfo.InvariantCulture)
            : key.ToString() ?? string.Empty;
    }
}
