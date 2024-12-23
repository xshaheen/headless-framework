// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Framework.Extensions;

namespace Framework.Specifications;

/// <summary>
/// Represents the combined specification which indicates that either the first specification
/// is satisfied by the given object or the second specification is not satisfied.
/// </summary>
/// <typeparam name="T">The type of the object to which the specification is applied.</typeparam>
public sealed class OrNotSpecification<T>(ISpecification<T> left, ISpecification<T> right)
    : CompositeSpecification<T>(left, right)
{
    public override Expression<Func<T, bool>> ToExpression()
    {
        return Left.ToExpression().OrNot(Right.ToExpression());
    }
}
