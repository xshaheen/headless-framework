// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Framework.Linq;

namespace Framework.Specifications;

/// <summary>
/// Represents the combined specification which indicates that either of the given
/// specification should be satisfied by the given object.
/// </summary>
/// <typeparam name="T">The type of the object to which the specification is applied.</typeparam>
/// <remarks>Initializes a new instance of <see cref="OrSpecification{T}"/> class.</remarks>
/// <param name="left">The first specification.</param>
/// <param name="right">The second specification.</param>
public sealed class OrSpecification<T>(ISpecification<T> left, ISpecification<T> right)
    : CompositeSpecification<T>(left, right)
{
    /// <summary>
    /// Gets the LINQ expression which represents the current specification.
    /// </summary>
    /// <returns>The LINQ expression.</returns>
    public override Expression<Func<T, bool>> ToExpression()
    {
        return Left.ToExpression().Or(Right.ToExpression());
    }
}
