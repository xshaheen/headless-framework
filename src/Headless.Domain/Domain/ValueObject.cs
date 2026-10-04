// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>
/// Provides a base class for domain-driven design value objects. Two instances are equal when all equality components are equal.
/// </summary>
/// <remarks>
/// Value objects have no persistent identity. Derived classes override equality hooks directly through <typeparamref name="TSelf"/>.
/// </remarks>
/// <typeparam name="TSelf">The concrete value object type.</typeparam>
[PublicAPI]
public abstract class ValueObject<TSelf> : EqualityBase<TSelf>, IValueObject
    where TSelf : ValueObject<TSelf>;

/// <summary>
/// Defines a marker interface for domain-driven design value objects whose identity is defined entirely by attribute values.
/// </summary>
[PublicAPI]
public interface IValueObject;
