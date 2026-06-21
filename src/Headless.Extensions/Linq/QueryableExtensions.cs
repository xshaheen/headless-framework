// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.Linq;

/// <summary>Extensions for conditionally applying <c>Where</c> filters to <see cref="IQueryable{T}"/> sources.</summary>
[PublicAPI]
public static class QueryableExtensions
{
    /// <summary>Filters a <see cref="IQueryable{T}"/> by given predicate if given condition is true.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="query">Queryable to apply filtering</param>
    /// <param name="condition">A boolean value</param>
    /// <param name="predicate">Predicate to filter the query</param>
    /// <returns>Filtered or not filtered query based on <paramref name="condition"/></returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is <see langword="null"/>.</exception>
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
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <typeparam name="TQueryable">The concrete queryable type, preserved in the return value.</typeparam>
    /// <param name="query">Queryable to apply filtering</param>
    /// <param name="condition">A boolean value</param>
    /// <param name="predicate">Predicate to filter the query</param>
    /// <returns>Filtered or not filtered query based on <paramref name="condition"/></returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is <see langword="null"/>.</exception>
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
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="query">Queryable to apply filtering</param>
    /// <param name="condition">A boolean value</param>
    /// <param name="predicate">Predicate (with element index) to filter the query</param>
    /// <returns>Filtered or not filtered query based on <paramref name="condition"/></returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is <see langword="null"/>.</exception>
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
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <typeparam name="TQueryable">The concrete queryable type, preserved in the return value.</typeparam>
    /// <param name="query">Queryable to apply filtering</param>
    /// <param name="condition">A boolean value</param>
    /// <param name="predicate">Predicate (with element index) to filter the query</param>
    /// <returns>Filtered or not filtered query based on <paramref name="condition"/></returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is <see langword="null"/>.</exception>
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
