namespace Framework.BuildingBlocks.Domains;

public class EntityEventData<TEntity>(TEntity entity)
{
    /// <summary>Related entity with this event.</summary>
    public TEntity Entity { get; } = entity;
}

/// <summary>
/// Used to pass data for an event when an entity (<see cref="IEntity"/>) is changed (created, updated, or deleted).
/// See <see cref="EntityCreatedEventData{TEntity}"/>, <see cref="EntityDeletedEventData{TEntity}"/> and <see cref="EntityUpdatedEventData{TEntity}"/> classes.
/// </summary>
/// <typeparam name="TEntity">Entity type</typeparam>
public class EntityChangedEventData<TEntity>(TEntity entity) : EntityEventData<TEntity>(entity);

/// <summary>This type of event can be used to notify just after the creation of an Entity.</summary>
/// <typeparam name="TEntity">Entity type</typeparam>
public class EntityCreatedEventData<TEntity>(TEntity entity) : EntityChangedEventData<TEntity>(entity);

/// <summary>This type of event can be used to notify just after the update of an Entity.</summary>
/// <typeparam name="TEntity">Entity type</typeparam>
public class EntityUpdatedEventData<TEntity>(TEntity entity) : EntityChangedEventData<TEntity>(entity);

/// <summary>This type of event can be used to notify just after the deletion of an Entity.</summary>
/// <typeparam name="TEntity">Entity type</typeparam>
public class EntityDeletedEventData<TEntity>(TEntity entity) : EntityChangedEventData<TEntity>(entity);
