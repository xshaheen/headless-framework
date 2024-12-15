// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Framework.Specifications.Internals;

namespace Framework.Specifications;

/// <summary>
/// Represents the combined specification which indicates that the first specification
/// can be satisifed by the given object whereas the second one cannot.
/// </summary>
/// <typeparam name="T">The type of the object to which the specification is applied.</typeparam>
public sealed class AndNotSpecification<T>(ISpecification<T> left, ISpecification<T> right)
    : CompositeSpecification<T>(left, right)
{
    public override Expression<Func<T, bool>> ToExpression()
    {
        var rightExpression = Right.ToExpression();

        var bodyNot = Expression.Not(rightExpression.Body);
        var bodyNotExpression = Expression.Lambda<Func<T, bool>>(bodyNot, rightExpression.Parameters);

        return Left.ToExpression().And(bodyNotExpression);
    }
}
