// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.EntityFramework;
using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>Extension method for materializing a data-grid query result.</summary>
[PublicAPI]
public static class DataGridExtensions
{
    /// <summary>
    /// Applies the ordering and paging from <paramref name="request"/> to the query and materializes the
    /// result as an <c>IndexPage</c>.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The source queryable.</param>
    /// <param name="request">The data-grid request carrying sort and page parameters.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A paged result set.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="request"/> is <see langword="null"/>.
    /// </exception>
    public static ValueTask<IndexPage<T>> ToDataGridAsync<T>(
        this IQueryable<T> source,
        IDataGridRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(source);
        Argument.IsNotNull(request);

        var query = source;

        if (request.Orders is { Count: > 0 })
        {
            query = source.OrderBy(request.Orders);
        }

        return query.ToIndexPageAsync(request.Page, cancellationToken);
    }
}
