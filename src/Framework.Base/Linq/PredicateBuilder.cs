// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;

namespace Framework.Linq;

/// <summary>
/// Predicate builder.
/// This is part of the solution which solves the expression parameter problem when going to Entity Framework.
/// For more information about this solution, please refer to https://learn.microsoft.com/en-us/archive/blogs/meek/linq-to-entities-combining-predicates
/// </summary>
[PublicAPI]
public static class PredicateBuilder
{
    public static Expression<Func<T, bool>> True<T>() => _ => true;

    public static Expression<Func<T, bool>> False<T>() => _ => false;

    public static Expression<Func<T, bool>> Or<T>(this IEnumerable<Expression<Func<T, bool>>> expressions)
    {
        return expressions.Aggregate(seed: False<T>(), (current, expression) => current.Or(expression));
    }

    public static Expression<Func<T, bool>> And<T>(this IEnumerable<Expression<Func<T, bool>>> expressions)
    {
        return expressions.Aggregate(seed: True<T>(), (current, expression) => current.And(expression));
    }

    public static Expression<Func<T, bool>> AndNot<T>(this Expression<Func<T, bool>> f, Expression<Func<T, bool>> s)
    {
        return f.And(s.Not());
    }

    public static Expression<Func<T, bool>> OrNot<T>(this Expression<Func<T, bool>> f, Expression<Func<T, bool>> s)
    {
        return f.Or(s.Not());
    }

    /// <summary>Negates the given expression by applying a logical NOT operation.</summary>
    /// <typeparam name="T">The type of the parameter in the expression.</typeparam>
    /// <param name="expression">The expression to be negated.</param>
    /// <returns>The negated expression.</returns>
    public static Expression<Func<T, bool>> Not<T>(this Expression<Func<T, bool>> expression)
    {
        return Expression.Lambda<Func<T, bool>>(Expression.Not(expression.Body), expression.Parameters);
    }

    /// <summary>Combines two given expressions by using the AND.</summary>
    /// <typeparam name="T">The type of the object.</typeparam>
    /// <param name="f">The first part of the expression.</param>
    /// <param name="s">The second part of the expression.</param>
    /// <returns>The combined expression.</returns>
    public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> f, Expression<Func<T, bool>> s)
    {
        return f._Compose(s, Expression.AndAlso);
    }

    /// <summary>Combines two given expressions by using the OR.</summary>
    /// <typeparam name="T">The type of the object.</typeparam>
    /// <param name="f">The first part of the expression.</param>
    /// <param name="s">The second part of the expression.</param>
    /// <returns>The combined expression.</returns>
    public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> f, Expression<Func<T, bool>> s)
    {
        return f._Compose(s, Expression.OrElse);
    }

    #region Helpers

    private static Expression<T> _Compose<T>(
        this Expression<T> first,
        Expression<T> second,
        Func<Expression, Expression, Expression> merge
    )
    {
        // build parameter map (from parameters of second to parameters of first)
        Dictionary<ParameterExpression, ParameterExpression> map = new(second.Parameters.Count);
        for (var i = 0; i < second.Parameters.Count; i++)
        {
            map.Add(second.Parameters[i], first.Parameters[i]);
        }

        // replace parameters in the second lambda expression with parameters from the first
        var secondBody = ParameterRebinder.ReplaceParameters(map, second.Body);

        // apply composition of lambda expression bodies to parameters from the first expression
        return Expression.Lambda<T>(merge(first.Body, secondBody), first.Parameters);
    }

    /// <summary>
    /// Represents the parameter rebinder used for rebinding the parameters for the given expressions.
    /// This is part of the solution which solves the expression parameter problem when going to Entity Framework.
    /// For more information about this solution, please refer to <a href="https://learn.microsoft.com/en-us/archive/blogs/meek/linq-to-entities-combining-predicates"></a>.
    /// </summary>
    private sealed class ParameterRebinder(Dictionary<ParameterExpression, ParameterExpression>? m) : ExpressionVisitor
    {
        private readonly Dictionary<ParameterExpression, ParameterExpression> _map = m ?? [];

        internal static Expression ReplaceParameters(
            Dictionary<ParameterExpression, ParameterExpression> map,
            Expression exp
        )
        {
            return new ParameterRebinder(map).Visit(exp);
        }

        protected override Expression VisitParameter(ParameterExpression p)
        {
            if (_map.TryGetValue(p, out var replacement))
            {
                p = replacement;
            }

            return base.VisitParameter(p);
        }
    }

    #endregion
}
