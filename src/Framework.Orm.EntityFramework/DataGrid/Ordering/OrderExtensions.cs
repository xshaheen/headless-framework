// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Primitives;

public static class OrderExtensions
{
    public static IOrderedQueryable<T> OrderBy<T>(this IQueryable<T> source, params List<OrderBy> orders)
    {
        return OrderBy(source, orders.AsReadOnlySpan());
    }

    public static IOrderedQueryable<T> OrderBy<T>(this IQueryable<T> source, params ReadOnlySpan<OrderBy> orders)
    {
        if (orders.IsEmpty)
        {
            return source.Order();
        }

        var (property, asc) = orders[0];

        var query = asc ? source.OrderBy(property) : source.OrderByDescending(property);

        for (var index = 1; index < orders.Length; ++index)
        {
            query = orders[index].Ascending
                ? source.ThenBy(orders[index].Property)
                : source.ThenByDescending(orders[index].Property);
        }

        return query;
    }

    /// <summary>Sorts the elements of a sequence in ascending order.</summary>
    /// <exception cref="ArgumentException">If <paramref name="propertyName"/> not valid property name.</exception>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">Source</param>
    /// <param name="propertyName">The property name to order by. You can use '.' to access a child property.</param>
    /// <param name="comparer">An <see cref="IComparer{T}"/> to compare keys.</param>
    public static IOrderedQueryable<T> OrderBy<T>(
        this IQueryable<T> source,
        string propertyName,
        IComparer<object>? comparer = null
    )
    {
        return _CallOrderedQueryable(source, nameof(Queryable.OrderBy), propertyName, comparer);
    }

    /// <summary>Sorts the elements of a sequence in descending order.</summary>
    /// <exception cref="ArgumentException">If <paramref name="propertyName"/> not valid property name.</exception>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">Source</param>
    /// <param name="propertyName">The property name to order by. You can use '.' to access a child property.</param>
    /// <param name="comparer">An <see cref="IComparer{T}"/> to compare keys.</param>
    public static IOrderedQueryable<T> OrderByDescending<T>(
        this IQueryable<T> source,
        string propertyName,
        IComparer<object>? comparer = null
    )
    {
        return _CallOrderedQueryable(source, nameof(Queryable.OrderByDescending), propertyName, comparer);
    }

    /// <summary>Performs a subsequent ordering of the elements in a sequence in ascending order.</summary>
    /// <exception cref="ArgumentException">If <paramref name="propertyName"/> not valid property name.</exception>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">Source</param>
    /// <param name="propertyName">The property name to order by. You can use '.' to access a child property.</param>
    /// <param name="comparer">An <see cref="IComparer{T}"/> to compare keys.</param>
    public static IOrderedQueryable<T> ThenBy<T>(
        this IQueryable<T> source,
        string propertyName,
        IComparer<object>? comparer = null
    )
    {
        return _CallOrderedQueryable(source, nameof(Queryable.ThenBy), propertyName, comparer);
    }

    /// <summary>Performs a subsequent ordering of the elements in a sequence in descending order.</summary>
    /// <exception cref="ArgumentException">If <paramref name="propertyName"/> not valid property name.</exception>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">Source</param>
    /// <param name="propertyName">The property name to order by. You can use '.' to access a child property.</param>
    /// <param name="comparer">An <see cref="IComparer{T}"/> to compare keys.</param>
    public static IOrderedQueryable<T> ThenByDescending<T>(
        this IQueryable<T> source,
        string propertyName,
        IComparer<object>? comparer = null
    )
    {
        return _CallOrderedQueryable(source, nameof(Queryable.ThenByDescending), propertyName, comparer);
    }

    /// <summary>Builds the Queryable functions using a TSource property name.</summary>
    private static IOrderedQueryable<T> _CallOrderedQueryable<T>(
        this IQueryable<T> source,
        string methodName,
        string propertyName,
        IComparer<object>? comparer = null
    )
    {
        var parameterExpression = Expression.Parameter(typeof(T), "x");

        Expression body;

        try
        {
            body = propertyName
                .Split('.')
                .Aggregate<string, Expression>(parameterExpression, Expression.PropertyOrField);
        }
        catch (Exception e)
        {
            throw new InvalidOrderPropertyException($"'{propertyName}' is invalid sorting property.", e);
        }

        return comparer is not null
            ? (IOrderedQueryable<T>)
                source.Provider.CreateQuery(
                    Expression.Call(
                        typeof(Queryable),
                        methodName,
                        [typeof(T), body.Type],
                        source.Expression,
                        Expression.Lambda(body, parameterExpression),
                        Expression.Constant(comparer)
                    )
                )
            : (IOrderedQueryable<T>)
                source.Provider.CreateQuery(
                    Expression.Call(
                        typeof(Queryable),
                        methodName,
                        [typeof(T), body.Type],
                        source.Expression,
                        Expression.Lambda(body, parameterExpression)
                    )
                );
    }
}

public sealed class InvalidOrderPropertyException(string message, Exception? inner) : Exception(message, inner);
