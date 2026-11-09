// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Fencing;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// Lets a job predicate test a row's lease for liveness inside a larger expression, judged by the database clock.
/// </summary>
/// <remarks>
/// <c>WhereLeaseLive</c> only appends a whole <c>Where</c>, and the clock function it calls is not public, but the
/// acquire, peek, and fallback predicates need "no live lease" as one arm of an <c>OR</c>. So the predicate is
/// written against the <see cref="IsLive"/> marker and <see cref="Expand{TEntity}"/> swaps each marker call for the
/// exact expression <c>WhereLeaseLive</c> builds, so both translate to the same SQL.
/// </remarks>
internal static class JobsLeaseExpressions
{
    private static readonly LambdaExpression _Live = _ExtractLive();

    /// <summary>A marker for the database-clock liveness test; only valid inside a predicate passed to <see cref="Expand{TEntity}"/>.</summary>
    /// <exception cref="InvalidOperationException">Always, when called outside a query.</exception>
    public static bool IsLive(RowLeaseSnapshot lease)
    {
        throw new InvalidOperationException(
            $"{nameof(JobsLeaseExpressions)}.{nameof(IsLive)} is only valid inside a predicate passed to {nameof(Expand)}."
        );
    }

    /// <summary>Replaces every <see cref="IsLive"/> call in <paramref name="predicate"/> with the database-clock test.</summary>
    public static Expression<Func<TEntity, bool>> Expand<TEntity>(Expression<Func<TEntity, bool>> predicate)
    {
        return (Expression<Func<TEntity, bool>>)new MarkerExpander().Visit(predicate);
    }

    private static LambdaExpression _ExtractLive()
    {
        // WhereLeaseLive appends Queryable.Where(source, Quote(lease => <live test>)); building it over an empty
        // in-memory query only constructs the tree, it never runs the clock function.
        var query = Enumerable.Empty<RowLeaseSnapshot>().AsQueryable().WhereLeaseLive(static lease => lease);
        var where = (MethodCallExpression)query.Expression;

        return (LambdaExpression)((UnaryExpression)where.Arguments[1]).Operand;
    }

    private sealed class MarkerExpander : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (
                node.Method.DeclaringType != typeof(JobsLeaseExpressions)
                || !string.Equals(node.Method.Name, nameof(IsLive), StringComparison.Ordinal)
            )
            {
                return base.VisitMethodCall(node);
            }

            var lease = Visit(node.Arguments[0]);

            return new ParameterReplacer(_Live.Parameters[0], lease).Visit(_Live.Body);
        }
    }

    private sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == parameter ? replacement : base.VisitParameter(node);
        }
    }
}
