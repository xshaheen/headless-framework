// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Framework.Checks;
using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Primitives;

[PublicAPI]
public static class IndexPageExtensions
{
    public static async ValueTask<IndexPage<T>> ToIndexPageAsync<T>(
        this IQueryable<T> source,
        CancellationToken cancellationToken = default
    )
    {
        var items = await source.ToListAsync(cancellationToken);

        return new(items, 0, items.Count, items.Count);
    }

    public static async ValueTask<IndexPage<T>> ToIndexPageAsync<T>(
        this IQueryable<T> source,
        int index,
        int size,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(size);

        var total = await source.CountAsync(cancellationToken);

        if (total == 0)
        {
            return new IndexPage<T>([], index, size, total);
        }

        var items =
            index < 0
                ? await source.SkipLast(-(index + 1) * size).TakeLast(size).ToListAsync(cancellationToken)
                : await source.Skip(index * size).Take(size).ToListAsync(cancellationToken);

        return new IndexPage<T>(items, index, size, total);
    }

    public static ValueTask<IndexPage<T>> ToIndexPageAsync<T>(
        this IQueryable<T> source,
        int? index,
        int? size,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(size);

        return index.HasValue && size.HasValue
            ? source.ToIndexPageAsync(index.Value, size.Value, cancellationToken)
            : source.ToIndexPageAsync(cancellationToken);
    }

    public static ValueTask<IndexPage<T>> ToIndexPageAsync<T>(
        this IQueryable<T> source,
        IIndexPageRequest? request,
        CancellationToken cancellationToken = default
    )
    {
        return request is null
            ? source.ToIndexPageAsync(cancellationToken)
            : source.ToIndexPageAsync(request.Index, request.Size, cancellationToken);
    }

    public static async ValueTask<IndexPage<TResult>> ToIndexPageAsync<TSource, TKey, TResult>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> orderBy,
        bool ascending,
        Expression<Func<TSource, TResult>> selector,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(source);
        Argument.IsNotNull(orderBy);
        Argument.IsNotNull(selector);

        var orderQuery = ascending ? source.OrderBy(orderBy) : source.OrderByDescending(orderBy);
        var projectedQuery = orderQuery.Select(selector);

        var items = await projectedQuery.ToListAsync(cancellationToken);

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
            return new([], index, size, total);
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

    public static async ValueTask<IndexPage<TResult>> ToIndexPageAsync<TSource, TKey, TResult>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> orderBy,
        bool ascending,
        Expression<Func<TSource, TResult>> selector,
        int? index,
        int? size,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(size);

        return index.HasValue && size.HasValue
            ? await source.ToIndexPageAsync(orderBy, ascending, selector, index.Value, size.Value, cancellationToken)
            : await source.ToIndexPageAsync(orderBy, ascending, selector, cancellationToken);
    }

    public static ValueTask<IndexPage<TResult>> ToIndexPageAsync<TSource, TKey, TResult>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> orderBy,
        bool ascending,
        Expression<Func<TSource, TResult>> selector,
        IIndexPageRequest? request,
        CancellationToken cancellationToken = default
    )
    {
        return request is null
            ? source.ToIndexPageAsync(orderBy, ascending, selector, cancellationToken)
            : source.ToIndexPageAsync(orderBy, ascending, selector, request.Index, request.Size, cancellationToken);
    }

    public static async ValueTask<IndexPage<TSource>> ToIndexPageAsync<TSource, TKey>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> orderBy,
        bool ascending,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(source);
        Argument.IsNotNull(orderBy);

        var total = await source.CountAsync(cancellationToken);

        if (total == 0)
        {
            return new([], 0, 0, 0);
        }

        var orderQuery = ascending ? source.OrderBy(orderBy) : source.OrderByDescending(orderBy);

        var items = await orderQuery.ToListAsync(cancellationToken);

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
            return new([], index, size, total);
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
        int? size,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(source);
        Argument.IsPositive(size);

        if (index.HasValue && size.HasValue)
        {
            return await source.ToIndexPageAsync(orderBy, ascending, index.Value, size.Value, cancellationToken);
        }

        var orderQuery = ascending ? source.OrderBy(orderBy) : source.OrderByDescending(orderBy);

        var items = await orderQuery.ToListAsync(cancellationToken);

        return new(items, 0, items.Count, items.Count);
    }

    public static ValueTask<IndexPage<TSource>> ToIndexPageAsync<TSource, TKey>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> orderBy,
        bool ascending,
        IIndexPageRequest? request,
        CancellationToken cancellationToken = default
    )
    {
        return request is null
            ? source.ToIndexPageAsync(orderBy, ascending, cancellationToken)
            : source.ToIndexPageAsync(orderBy, ascending, request.Index, request.Size, cancellationToken);
    }
}
