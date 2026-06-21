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
/// Base class for DDD value objects. Two instances are equal when all of their
/// <c>EqualityComponents()</c> values are equal; neither instance needs a dedicated identity field.
/// </summary>
[PublicAPI]
public abstract class ValueObject : EqualityBase<ValueObject>, IValueObject;
