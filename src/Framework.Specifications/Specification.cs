// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;

namespace Framework.Specifications;

/// <summary>
/// Represents that the implemented classes are specifications. For more
/// information about the specification pattern, please refer to http://martinfowler.com/apsupp/spec.pdf.
/// </summary>
/// <typeparam name="T">The type of the object to which the specification is applied.</typeparam>
public interface ISpecification<T>
{
    /// <summary>
    /// Returns a <see cref="bool"/> value which indicates whether the specification is satisfied by the given object.
    /// </summary>
    /// <param name="obj">The object to which the specification is applied.</param>
    /// <returns>True if the specification is satisfied, otherwise false.</returns>
    bool IsSatisfiedBy(T obj);

    /// <summary>Gets the LINQ expression which represents the current specification.</summary>
    /// <returns>The LINQ expression.</returns>
    Expression<Func<T, bool>> ToExpression();
}

/// <summary>Represents the base class for specifications.</summary>
/// <typeparam name="T">The type of the object to which the specification is applied.</typeparam>
public abstract class Specification<T> : ISpecification<T>
{
    public virtual bool IsSatisfiedBy(T obj)
    {
        return ToExpression().Compile()(obj);
    }

    public abstract Expression<Func<T, bool>> ToExpression();

    public static implicit operator Expression<Func<T, bool>>(Specification<T> specification)
    {
        return specification.ToExpression();
    }

    public static implicit operator Specification<T>(Expression<Func<T, bool>> operand)
    {
        return new ExpressionSpecification<T>(operand);
    }

    public static Specification<T> FromExpression(Expression<Func<T, bool>> operand)
    {
        return new ExpressionSpecification<T>(operand);
    }
}
