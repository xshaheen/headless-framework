// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Specifications;

/// <summary>Represents that the implemented classes are composite specifications.</summary>
/// <typeparam name="T">The type of the object to which the specification is applied.</typeparam>
public interface ICompositeSpecification<T> : ISpecification<T>
{
    /// <summary>
    /// Gets the left side of the specification.
    /// </summary>
    ISpecification<T> Left { get; }

    /// <summary>
    /// Gets the right side of the specification.
    /// </summary>
    ISpecification<T> Right { get; }
}

/// <summary>Represents the base class for composite specifications.</summary>
/// <typeparam name="T">The type of the object to which the specification is applied.</typeparam>
/// <remarks>Constructs a new instance of <see cref="CompositeSpecification{T}"/> class.</remarks>
/// <param name="left">The first specification.</param>
/// <param name="right">The second specification.</param>
public abstract class CompositeSpecification<T>(ISpecification<T> left, ISpecification<T> right)
    : Specification<T>,
        ICompositeSpecification<T>
{
    public ISpecification<T> Left { get; } = left;

    public ISpecification<T> Right { get; } = right;
}
