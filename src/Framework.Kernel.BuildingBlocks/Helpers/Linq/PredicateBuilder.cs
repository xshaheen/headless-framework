using System.Linq.Expressions;

namespace Framework.Kernel.BuildingBlocks.Helpers.Linq;

[PublicAPI]
public static class PredicateBuilder
{
    public static Expression<Func<T, bool>> True<T>() => _ => true;

    public static Expression<Func<T, bool>> False<T>() => _ => false;

    public static Expression<Func<T, bool>> Not<T>(this Expression<Func<T, bool>> expression)
    {
        var invoke = Expression.Invoke(expression, expression.Parameters);

        return Expression.Lambda<Func<T, bool>>(Expression.Not(invoke), expression.Parameters);
    }

    public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> e1, Expression<Func<T, bool>> e2)
    {
        var invokedExpr = Expression.Invoke(e2, e1.Parameters);

        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(e1.Body, invokedExpr), e1.Parameters);
    }

    public static Expression<Func<T, bool>> Or<T>(this IEnumerable<Expression<Func<T, bool>>> expressions)
    {
        var result = False<T>();

        foreach (var expression in expressions)
        {
            result = result.Or(expression);
        }

        return result;
    }

    public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> e1, Expression<Func<T, bool>> e2)
    {
        var invokedExpr = Expression.Invoke(e2, e1.Parameters);

        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(e1.Body, invokedExpr), e1.Parameters);
    }

    public static Expression<Func<T, bool>> And<T>(this IEnumerable<Expression<Func<T, bool>>> expressions)
    {
        var result = True<T>();

        foreach (var expression in expressions)
        {
            result = result.And(expression);
        }

        return result;
    }

    public static Expression<Func<T, bool>> AndNot<T>(this Expression<Func<T, bool>> e1, Expression<Func<T, bool>> e2)
    {
        return e1.And(e2.Not());
    }

    public static Expression<Func<T, bool>> OrNot<T>(this Expression<Func<T, bool>> e1, Expression<Func<T, bool>> e2)
    {
        return e1.Or(e2.Not());
    }
}
