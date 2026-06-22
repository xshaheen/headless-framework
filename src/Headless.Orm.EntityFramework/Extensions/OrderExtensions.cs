// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Primitives;

/// <summary>
/// Thrown when a sort property name passed to a string-based ordering extension does not correspond
/// to a valid property path on the entity type.
/// </summary>
public sealed class InvalidOrderPropertyException(string message, Exception? inner) : Exception(message, inner);

/// <summary>
/// Extension members for applying dynamic, string-based or <c>OrderBy</c>-descriptor ordering to an
/// <see cref="IQueryable{T}"/>.
/// </summary>
public static class OrderExtensions
{
    extension<T>(IQueryable<T> source)
    {
        /// <summary>
        /// Applies the ordering descriptors from a list, chaining each after the previous using the
        /// appropriate <c>OrderBy</c> / <c>ThenBy</c> variant.
        /// </summary>
        /// <param name="orders">Ordered list of sort descriptors to apply.</param>
        /// <returns>An ordered queryable.</returns>
        public IOrderedQueryable<T> OrderBy(List<OrderBy> orders)
        {
            return source.OrderBy(orders.AsReadOnlySpan());
        }

        /// <summary>
        /// Applies the ordering descriptors from a span, chaining each after the previous using the
        /// appropriate <c>OrderBy</c> / <c>ThenBy</c> variant. An empty span returns the source ordered
        /// by its natural order.
        /// </summary>
        /// <param name="orders">Span of sort descriptors to apply.</param>
        /// <returns>An ordered queryable.</returns>
        [OverloadResolutionPriority(1)]
        public IOrderedQueryable<T> OrderBy(params ReadOnlySpan<OrderBy> orders)
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
                    ? query.ThenBy(orders[index].Property)
                    : query.ThenByDescending(orders[index].Property);
            }

            return query;
        }

        /// <summary>Sorts the elements of a sequence in ascending order.</summary>
        /// <exception cref="ArgumentException">If <paramref name="propertyName"/> not valid property name.</exception>
        /// <param name="propertyName">The property name to order by. You can use '.' to access a child property.</param>
        /// <param name="comparer">An <see cref="IComparer{T}"/> to compare keys.</param>
        public IOrderedQueryable<T> OrderBy(string propertyName, IComparer<object>? comparer = null)
        {
            return source._CallOrderedQueryable(nameof(Queryable.OrderBy), propertyName, comparer);
        }

        /// <summary>Sorts the elements of a sequence in descending order.</summary>
        /// <exception cref="ArgumentException">If <paramref name="propertyName"/> not valid property name.</exception>
        /// <param name="propertyName">The property name to order by. You can use '.' to access a child property.</param>
        /// <param name="comparer">An <see cref="IComparer{T}"/> to compare keys.</param>
        public IOrderedQueryable<T> OrderByDescending(string propertyName, IComparer<object>? comparer = null)
        {
            return source._CallOrderedQueryable(nameof(Queryable.OrderByDescending), propertyName, comparer);
        }

        /// <summary>Performs a subsequent ordering of the elements in a sequence in ascending order.</summary>
        /// <exception cref="ArgumentException">If <paramref name="propertyName"/> not valid property name.</exception>
        /// <param name="propertyName">The property name to order by. You can use '.' to access a child property.</param>
        /// <param name="comparer">An <see cref="IComparer{T}"/> to compare keys.</param>
        public IOrderedQueryable<T> ThenBy(string propertyName, IComparer<object>? comparer = null)
        {
            return source._CallOrderedQueryable(nameof(Queryable.ThenBy), propertyName, comparer);
        }

        /// <summary>Performs a subsequent ordering of the elements in a sequence in descending order.</summary>
        /// <exception cref="ArgumentException">If <paramref name="propertyName"/> not valid property name.</exception>
        /// <param name="propertyName">The property name to order by. You can use '.' to access a child property.</param>
        /// <param name="comparer">An <see cref="IComparer{T}"/> to compare keys.</param>
        public IOrderedQueryable<T> ThenByDescending(string propertyName, IComparer<object>? comparer = null)
        {
            return source._CallOrderedQueryable(nameof(Queryable.ThenByDescending), propertyName, comparer);
        }

        /// <summary>Builds the Queryable functions using a TSource property name.</summary>
        private IOrderedQueryable<T> _CallOrderedQueryable(
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
}
