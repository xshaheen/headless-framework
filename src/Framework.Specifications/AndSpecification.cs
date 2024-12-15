// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Framework.Specifications.Internals;

namespace Framework.Specifications;

/// <summary>
/// Represents the combined specification which indicates that both of the given
/// specifications should be satisfied by the given object.
/// </summary>
/// <typeparam name="T">The type of the object to which the specification is applied.</typeparam>
public sealed class AndSpecification<T>(ISpecification<T> left, ISpecification<T> right)
    : CompositeSpecification<T>(left, right)
{
    public override Expression<Func<T, bool>> ToExpression()
    {
        return Left.ToExpression().And(Right.ToExpression());
    }
}
