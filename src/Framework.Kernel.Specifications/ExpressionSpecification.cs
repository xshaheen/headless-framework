// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Linq.Expressions;

namespace Framework.Kernel.Specifications;

/// <summary>Represents the specification which is represented by the corresponding LINQ expression.</summary>
/// <typeparam name="T">The type of the object to which the specification is applied.</typeparam>
/// <remarks>Initializes a new instance of <c>ExpressionSpecification&lt;T&gt;</c> class.</remarks>
/// <param name="expression">The LINQ expression which represents the current specification.</param>
public sealed class ExpressionSpecification<T>(Expression<Func<T, bool>> expression) : Specification<T>
{
    public override Expression<Func<T, bool>> ToExpression() => expression;
}
