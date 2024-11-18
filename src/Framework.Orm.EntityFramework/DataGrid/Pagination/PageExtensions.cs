// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Framework.Checks;
using Microsoft.EntityFrameworkCore;

namespace Framework.Orm.EntityFramework.DataGrid.Pagination;

public static class PageExtensions
{
    public static async ValueTask<IndexPage<T>> ToIndexPageAsync<T>(
        this IQueryable<T> source,
        int index,
        int size,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(source);
        Argument.IsPositive(size);

        var total = await source.CountAsync(cancellationToken);

        if (total == 0)
        {
            return new IndexPage<T>(Array.Empty<T>(), index, size, total);
        }

        var items =
            index < 0
                ? await source.SkipLast(-(index + 1) * size).TakeLast(size).ToListAsync(cancellationToken)
                : await source.Skip(index * size).Take(size).ToListAsync(cancellationToken);

        return new IndexPage<T>(items, index, size, total);
    }

    public static async ValueTask<IndexPage<T>> ToIndexPageAsync<T>(
        this IQueryable<T> source,
        int? index,
        int size,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(source);
        Argument.IsPositive(size);

        if (index.HasValue)
        {
            return await source.ToIndexPageAsync(index.Value, size, cancellationToken);
        }

        var items = await source.ToListAsync(cancellationToken);

        return new IndexPage<T>(items, 0, items.Count, items.Count);
    }

    public static ValueTask<IndexPage<T>> ToIndexPageAsync<T>(
        this IQueryable<T> source,
        IIndexPageRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(source);
        Argument.IsNotNull(request);

        return source.ToIndexPageAsync(request.Index, request.Size, cancellationToken);
    }

    public static async ValueTask<IndexPage<TResult>> ToIndexPageAsync<TSource, TKey, TResult>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> orderBy,
        bool ascending,
        Expression<Func<TSource, TResult>> selector,
        int? index,
        int size,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(source);
        Argument.IsPositive(size);

        if (index.HasValue)
        {
            return await source.ToIndexPageAsync(orderBy, ascending, selector, index.Value, size, cancellationToken);
        }

        var orderQuery = ascending ? source.OrderBy(orderBy) : source.OrderByDescending(orderBy);
        var projectedQuery = orderQuery.Select(selector);

        var items = await projectedQuery.ToListAsync(cancellationToken);

        return new(items, 0, items.Count, items.Count);
    }

    public static async ValueTask<IndexPage<TSource>> ToIndexPageAsync<TSource, TKey>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> orderBy,
        bool ascending,
        int index,
        int size,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(source);
        Argument.IsNotNull(orderBy);
        Argument.IsPositive(size);

        var total = await source.CountAsync(cancellationToken);

        if (total == 0)
        {
            return new(Array.Empty<TSource>(), index, size, total);
        }

        var orderQuery = ascending ? source.OrderBy(orderBy) : source.OrderByDescending(orderBy);
        var pagedQuery =
            index < 0
                ? orderQuery.SkipLast(-(index + 1) * size).TakeLast(size)
                : orderQuery.Skip(index * size).Take(size);

        var items = await pagedQuery.ToListAsync(cancellationToken);

        return new(items, index, size, total);
    }

    public static async ValueTask<IndexPage<TSource>> ToIndexPageAsync<TSource, TKey>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> orderBy,
        bool ascending,
        int? index,
        int size,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(source);
        Argument.IsPositive(size);

        if (index.HasValue)
        {
            return await source.ToIndexPageAsync(orderBy, ascending, index.Value, size, cancellationToken);
        }

        var orderQuery = ascending ? source.OrderBy(orderBy) : source.OrderByDescending(orderBy);

        var items = await orderQuery.ToListAsync(cancellationToken);

        return new(items, 0, items.Count, items.Count);
    }

    public static async ValueTask<IndexPage<TResult>> ToIndexPageAsync<TSource, TKey, TResult>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> orderBy,
        bool ascending,
        Expression<Func<TSource, TResult>> selector,
        int index,
        int size,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(source);
        Argument.IsNotNull(orderBy);
        Argument.IsNotNull(selector);
        Argument.IsPositive(size);

        var total = await source.CountAsync(cancellationToken);

        if (total == 0)
        {
            return new(Array.Empty<TResult>(), index, size, total);
        }

        var orderQuery = ascending ? source.OrderBy(orderBy) : source.OrderByDescending(orderBy);
        var pagedQuery =
            index < 0
                ? orderQuery.SkipLast(-(index + 1) * size).TakeLast(size)
                : orderQuery.Skip(index * size).Take(size);
        var projectedQuery = pagedQuery.Select(selector);

        var items = await projectedQuery.ToListAsync(cancellationToken);

        return new(items, index, size, total);
    }
}
