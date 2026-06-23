// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Marker interface for DDD value objects — types whose identity is defined entirely by their attribute values
/// rather than a persistent key.
/// </summary>
[PublicAPI]
public interface IValueObject;

/// <summary>
/// Base class for DDD value objects. Two instances are equal when all of their equality components
/// (declared via <c>EqualityComponentsEqual</c> / <c>BuildHashCode</c>) are equal; neither instance needs a
/// dedicated identity field.
/// </summary>
/// <remarks>
/// Self-typed (<c>TSelf</c>) so the equality hooks receive the concrete type directly, e.g.
/// <c>class Money : ValueObject&lt;Money&gt;</c> overrides <c>EqualityComponentsEqual(Money other)</c> with no
/// cast. Use the non-generic <see cref="IValueObject"/> marker to reference value objects heterogeneously.
/// </remarks>
/// <typeparam name="TSelf">The concrete value-object type.</typeparam>
[PublicAPI]
public abstract class ValueObject<TSelf> : EqualityBase<TSelf>, IValueObject
    where TSelf : ValueObject<TSelf>;
