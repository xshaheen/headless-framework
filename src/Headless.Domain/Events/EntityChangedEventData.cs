// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>
/// Carries domain event data when an entity implementing <see cref="IEntity"/> is created, updated, or deleted.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
[PublicAPI]
public sealed class EntityChangedEventData<TEntity>(TEntity entity) : EntityEventData<TEntity>(entity);
