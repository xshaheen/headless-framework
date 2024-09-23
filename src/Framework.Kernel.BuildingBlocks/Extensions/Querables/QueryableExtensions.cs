// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Linq.Expressions;
using Framework.Kernel.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Linq;

[PublicAPI]
public static class QueryableExtensions
{
    /// <summary>Filters a <see cref="IQueryable{T}"/> by given predicate if given condition is true.</summary>
    /// <param name="query">Queryable to apply filtering</param>
    /// <param name="condition">A boolean value</param>
    /// <param name="predicate">Predicate to filter the query</param>
    /// <returns>Filtered or not filtered query based on <paramref name="condition"/></returns>
    [MustUseReturnValue]
    public static IQueryable<T> WhereIf<T>(
        this IQueryable<T> query,
        bool condition,
        Expression<Func<T, bool>> predicate
    )
    {
        Argument.IsNotNull(query);

        return condition ? query.Where(predicate) : query;
    }

    /// <summary>Filters a <see cref="IQueryable{T}"/> by given predicate if given condition is true.</summary>
    /// <param name="query">Queryable to apply filtering</param>
    /// <param name="condition">A boolean value</param>
    /// <param name="predicate">Predicate to filter the query</param>
    /// <returns>Filtered or not filtered query based on <paramref name="condition"/></returns>
    [MustUseReturnValue]
    public static TQueryable WhereIf<T, TQueryable>(
        this TQueryable query,
        bool condition,
        Expression<Func<T, bool>> predicate
    )
        where TQueryable : IQueryable<T>
    {
        Argument.IsNotNull(query);

        return condition ? (TQueryable)query.Where(predicate) : query;
    }

    /// <summary>Filters a <see cref="IQueryable{T}"/> by given predicate if given condition is true.</summary>
    /// <param name="query">Queryable to apply filtering</param>
    /// <param name="condition">A boolean value</param>
    /// <param name="predicate">Predicate to filter the query</param>
    /// <returns>Filtered or not filtered query based on <paramref name="condition"/></returns>
    [MustUseReturnValue]
    public static IQueryable<T> WhereIf<T>(
        this IQueryable<T> query,
        bool condition,
        Expression<Func<T, int, bool>> predicate
    )
    {
        Argument.IsNotNull(query);

        return condition ? query.Where(predicate) : query;
    }

    /// <summary>Filters a <see cref="IQueryable{T}"/> by given predicate if given condition is true.</summary>
    /// <param name="query">Queryable to apply filtering</param>
    /// <param name="condition">A boolean value</param>
    /// <param name="predicate">Predicate to filter the query</param>
    /// <returns>Filtered or not filtered query based on <paramref name="condition"/></returns>
    [MustUseReturnValue]
    public static TQueryable WhereIf<T, TQueryable>(
        this TQueryable query,
        bool condition,
        Expression<Func<T, int, bool>> predicate
    )
        where TQueryable : IQueryable<T>
    {
        Argument.IsNotNull(query);

        return condition ? (TQueryable)query.Where(predicate) : query;
    }
}
