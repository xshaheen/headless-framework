// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>Carries domain event data raised immediately after an entity is updated.</summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
[PublicAPI]
public sealed class EntityUpdatedEventData<TEntity>(TEntity entity) : EntityEventData<TEntity>(entity);
