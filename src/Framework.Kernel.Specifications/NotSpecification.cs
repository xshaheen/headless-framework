using System.Linq.Expressions;

namespace Framework.Kernel.Specifications;

/// <summary>Represents the specification which indicates the semantics opposite to the given specification.</summary>
/// <typeparam name="T">The type of the object to which the specification is applied.</typeparam>
/// <remarks>
/// Initializes a new instance of <see cref="NotSpecification{T}"/> class.
/// </remarks>
/// <param name="specification">The specification to be reversed.</param>
public class NotSpecification<T>(ISpecification<T> specification) : Specification<T>
{
    /// <summary>
    /// Gets the LINQ expression which represents the current specification.
    /// </summary>
    /// <returns>The LINQ expression.</returns>
    public override Expression<Func<T, bool>> ToExpression()
    {
        var expression = specification.ToExpression();
        return Expression.Lambda<Func<T, bool>>(Expression.Not(expression.Body), expression.Parameters);
    }
}
