// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Framework.Linq;

namespace Framework.Specifications;

/// <summary>Represents the specification which indicates the semantics opposite to the given specification.</summary>
/// <typeparam name="T">The type of the object to which the specification is applied.</typeparam>
/// <remarks>
/// Initializes a new instance of <see cref="NotSpecification{T}"/> class.
/// </remarks>
/// <param name="specification">The specification to be reversed.</param>
public sealed class NotSpecification<T>(ISpecification<T> specification) : Specification<T>
{
    /// <summary>
    /// Gets the LINQ expression which represents the current specification.
    /// </summary>
    /// <returns>The LINQ expression.</returns>
    public override Expression<Func<T, bool>> ToExpression()
    {
        return specification.ToExpression().Not();
    }
}
