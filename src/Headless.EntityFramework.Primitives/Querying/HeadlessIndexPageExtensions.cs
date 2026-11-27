// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Checks;
using Headless.Primitives;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension members for materializing <see cref="IQueryable{T}"/> results into paginated
/// <c>IndexPage</c> responses.
/// </summary>
/// <remarks>
/// Negative <c>index</c> values address pages from the end of the result set: index <c>-1</c>
/// returns the last page, <c>-2</c> the second-to-last, and so on. Every overload that takes an
/// index and size, or a page request, pages. Only the overloads without them read the full result
/// set, so an optional page parameter can never turn into an unbounded read.
/// </remarks>
[PublicAPI]
public static class HeadlessIndexPageExtensions
{
    extension<T>(IQueryable<T> source)
    {
        /// <summary>
        /// Materializes the full query result as a single <c>IndexPage</c> with no paging applied.
        /// </summary>
        /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
        /// <returns>An <c>IndexPage</c> containing all results.</returns>
        public async ValueTask<IndexPage<T>> ToIndexPageAsync(CancellationToken cancellationToken = default)
        {
            var items = await source.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new(items, 0, items.Count, items.Count);
        }

        /// <summary>
        /// Materializes a page of results at the given zero-based <paramref name="index"/> with the
        /// specified <paramref name="size"/>.
        /// </summary>
        /// <param name="index">Zero-based page index. Negative values address pages from the end.</param>
        /// <param name="size">Number of items per page. Must be positive.</param>
        /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
        /// <returns>An <c>IndexPage</c> containing the requested slice and the total count.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not positive.</exception>
        public async ValueTask<IndexPage<T>> ToIndexPageAsync(
            int index,
            int size,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsPositive(size);

            var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);

            if (total == 0)
            {
                return new IndexPage<T>([], index, size, total);
            }

            var pageIndex = _GetPageIndex(index, size, total);
            var skip = _GetSkipCount(pageIndex, size);

            if (skip is null)
            {
                return new([], pageIndex, size, total);
            }

            var items = await source.Skip(skip.Value).Take(size).ToListAsync(cancellationToken).ConfigureAwait(false);

            return new IndexPage<T>(items, pageIndex, size, total);
        }

        /// <summary>
        /// Materializes the page of results described by the <paramref name="request"/>.
        /// </summary>
        /// <param name="request">The page request carrying index and size.</param>
        /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
        /// <returns>An <c>IndexPage</c> containing the requested slice and the total count.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The request size is not positive.</exception>
        public ValueTask<IndexPage<T>> ToIndexPageAsync(
            IIndexPageRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(request);

            return source.ToIndexPageAsync(request.Index, request.Size, cancellationToken);
        }

        /// <summary>
        /// Materializes the full ordered result set, projected through <paramref name="selector"/>, as a single page.
        /// </summary>
        /// <param name="orderBy">Key selector for ordering.</param>
        /// <param name="ascending">When <see langword="true"/> orders ascending; otherwise descending.</param>
        /// <param name="selector">Projection applied after ordering.</param>
        /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
        /// <returns>An <c>IndexPage</c> containing all projected results.</returns>
        public async ValueTask<IndexPage<TResult>> ToIndexPageAsync<TKey, TResult>(
            Expression<Func<T, TKey>> orderBy,
            bool ascending,
            Expression<Func<T, TResult>> selector,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(source);
            Argument.IsNotNull(orderBy);
            Argument.IsNotNull(selector);

            var orderQuery = ascending ? source.OrderBy(orderBy) : source.OrderByDescending(orderBy);
            var projectedQuery = orderQuery.Select(selector);

            var items = await projectedQuery.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new(items, 0, items.Count, items.Count);
        }

        /// <summary>
        /// Materializes a page of ordered, projected results at the given zero-based <paramref name="index"/>.
        /// </summary>
        /// <param name="orderBy">Key selector for ordering.</param>
        /// <param name="ascending">When <see langword="true"/> orders ascending; otherwise descending.</param>
        /// <param name="selector">Projection applied after ordering.</param>
        /// <param name="index">Zero-based page index. Negative values address pages from the end.</param>
        /// <param name="size">Number of items per page. Must be positive.</param>
        /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
        /// <returns>An <c>IndexPage</c> containing the requested slice and the total count.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not positive.</exception>
        public async ValueTask<IndexPage<TResult>> ToIndexPageAsync<TKey, TResult>(
            Expression<Func<T, TKey>> orderBy,
            bool ascending,
            Expression<Func<T, TResult>> selector,
            int index,
            int size,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(source);
            Argument.IsNotNull(orderBy);
            Argument.IsNotNull(selector);
            Argument.IsPositive(size);

            var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);

            if (total == 0)
            {
                return new([], index, size, total);
            }

            var orderQuery = ascending ? source.OrderBy(orderBy) : source.OrderByDescending(orderBy);

            var pageIndex = _GetPageIndex(index, size, total);
            var skip = _GetSkipCount(pageIndex, size);

            if (skip is null)
            {
                return new([], pageIndex, size, total);
            }

            var pagedQuery = orderQuery.Skip(skip.Value).Take(size);

            var projectedQuery = pagedQuery.Select(selector);

            var items = await projectedQuery.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new(items, pageIndex, size, total);
        }

        /// <summary>
        /// Materializes the page of ordered, projected results described by the <paramref name="request"/>.
        /// </summary>
        /// <param name="orderBy">Key selector for ordering.</param>
        /// <param name="ascending">When <see langword="true"/> orders ascending; otherwise descending.</param>
        /// <param name="selector">Projection applied after ordering.</param>
        /// <param name="request">The page request carrying index and size.</param>
        /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
        /// <returns>An <c>IndexPage</c> containing the requested slice and the total count.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The request size is not positive.</exception>
        public ValueTask<IndexPage<TResult>> ToIndexPageAsync<TKey, TResult>(
            Expression<Func<T, TKey>> orderBy,
            bool ascending,
            Expression<Func<T, TResult>> selector,
            IIndexPageRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(request);

            return source.ToIndexPageAsync(
                orderBy,
                ascending,
                selector,
                request.Index,
                request.Size,
                cancellationToken
            );
        }

        /// <summary>
        /// Materializes the full ordered result set as a single <c>IndexPage</c> with no paging applied.
        /// </summary>
        /// <param name="orderBy">Key selector for ordering.</param>
        /// <param name="ascending">When <see langword="true"/> orders ascending; otherwise descending.</param>
        /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
        /// <returns>An <c>IndexPage</c> containing all ordered results.</returns>
        public async ValueTask<IndexPage<T>> ToIndexPageAsync<TKey>(
            Expression<Func<T, TKey>> orderBy,
            bool ascending,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(source);
            Argument.IsNotNull(orderBy);

            // No paging: the materialized list is the full result, so its count is the total —
            // a separate CountAsync round-trip would be pure waste (mirrors the no-order overload).
            var orderQuery = ascending ? source.OrderBy(orderBy) : source.OrderByDescending(orderBy);

            var items = await orderQuery.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new(items, 0, items.Count, items.Count);
        }

        /// <summary>
        /// Materializes a page of ordered results at the given zero-based <paramref name="index"/>
        /// with the specified <paramref name="size"/>.
        /// </summary>
        /// <param name="orderBy">Key selector for ordering.</param>
        /// <param name="ascending">When <see langword="true"/> orders ascending; otherwise descending.</param>
        /// <param name="index">Zero-based page index. Negative values address pages from the end.</param>
        /// <param name="size">Number of items per page. Must be positive.</param>
        /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
        /// <returns>An <c>IndexPage</c> containing the requested slice and the total count.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not positive.</exception>
        public async ValueTask<IndexPage<T>> ToIndexPageAsync<TKey>(
            Expression<Func<T, TKey>> orderBy,
            bool ascending,
            int index,
            int size,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(source);
            Argument.IsNotNull(orderBy);
            Argument.IsPositive(size);

            var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);

            if (total == 0)
            {
                return new([], index, size, total);
            }

            var orderQuery = ascending ? source.OrderBy(orderBy) : source.OrderByDescending(orderBy);

            var pageIndex = _GetPageIndex(index, size, total);
            var skip = _GetSkipCount(pageIndex, size);

            if (skip is null)
            {
                return new([], pageIndex, size, total);
            }

            var pagedQuery = orderQuery.Skip(skip.Value).Take(size);

            var items = await pagedQuery.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new(items, pageIndex, size, total);
        }

        /// <summary>
        /// Materializes the page of ordered results described by the <paramref name="request"/>.
        /// </summary>
        /// <param name="orderBy">Key selector for ordering.</param>
        /// <param name="ascending">When <see langword="true"/> orders ascending; otherwise descending.</param>
        /// <param name="request">The page request carrying index and size.</param>
        /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
        /// <returns>An <c>IndexPage</c> containing the requested slice and the total count.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The request size is not positive.</exception>
        public ValueTask<IndexPage<T>> ToIndexPageAsync<TKey>(
            Expression<Func<T, TKey>> orderBy,
            bool ascending,
            IIndexPageRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(request);

            return source.ToIndexPageAsync(orderBy, ascending, request.Index, request.Size, cancellationToken);
        }
    }

    private static int _GetPageIndex(int index, int size, int total)
    {
        return index >= 0 || total == 0 ? index : _GetTotalPages(total, size) + index;
    }

    private static int? _GetSkipCount(int pageIndex, int size)
    {
        if (pageIndex < 0)
        {
            return null;
        }

        var skip = (long)pageIndex * size;

        return skip > int.MaxValue ? null : (int)skip;
    }

    private static int _GetTotalPages(int total, int size)
    {
        return (int)Math.Ceiling(total / (decimal)size);
    }
}
