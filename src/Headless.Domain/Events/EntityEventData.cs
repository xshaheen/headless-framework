// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>
/// Provides a base implementation for domain events that carry a reference to the source entity.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
[PublicAPI]
public abstract class EntityEventData<TEntity>(TEntity entity)
{
    /// <summary>Gets the entity associated with this event.</summary>
    public TEntity Entity { get; } = entity;
}
