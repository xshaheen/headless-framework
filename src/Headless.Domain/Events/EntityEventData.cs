// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Base class for domain events that carry a reference to the entity that triggered the event.
/// </summary>
/// <typeparam name="TEntity">Type of the entity associated with the event.</typeparam>
[PublicAPI]
public abstract class EntityEventData<TEntity>(TEntity entity)
{
    /// <summary>Related entity with this event.</summary>
    public TEntity Entity { get; } = entity;
}
