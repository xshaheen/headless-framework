// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>This type of event can be used to notify just after the update of an Entity.</summary>
/// <typeparam name="TEntity">Entity type</typeparam>
[PublicAPI]
public sealed class EntityUpdatedEventData<TEntity>(TEntity entity) : EntityEventData<TEntity>(entity);
