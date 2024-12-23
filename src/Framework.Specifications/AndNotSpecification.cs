// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Framework.Extensions;

namespace Framework.Specifications;

/// <summary>
/// Represents the combined specification which indicates that the first specification
/// can be satisfied by the given object whereas the second one cannot.
/// </summary>
/// <typeparam name="T">The type of the object to which the specification is applied.</typeparam>
public sealed class AndNotSpecification<T>(ISpecification<T> left, ISpecification<T> right)
    : CompositeSpecification<T>(left, right)
{
    public override Expression<Func<T, bool>> ToExpression()
    {
        return Left.ToExpression().AndNot(Right.ToExpression());
    }
}
