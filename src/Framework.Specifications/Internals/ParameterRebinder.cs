// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;

namespace Framework.Specifications.Internals;

/// <summary>
/// Represents the parameter rebinder used for rebinding the parameters for the given expressions.
/// This is part of the solution which solves the expression parameter problem when going to Entity Framework.
/// For more information about this solution,
/// please refer to http://blogs.msdn.com/b/meek/archive/2008/05/02/linq-to-entities-combining-predicates.aspx.
/// </summary>
internal sealed class ParameterRebinder(Dictionary<ParameterExpression, ParameterExpression>? map) : ExpressionVisitor
{
    private readonly Dictionary<ParameterExpression, ParameterExpression> _map = map ?? [];

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
