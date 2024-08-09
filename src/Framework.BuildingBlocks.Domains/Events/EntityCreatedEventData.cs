namespace Framework.BuildingBlocks.Domains.Events;

/// <summary>This type of event can be used to notify just after the creation of an Entity.</summary>
/// <typeparam name="TEntity">Entity type</typeparam>
public class EntityCreatedEventData<TEntity>(TEntity entity) : EntityChangedEventData<TEntity>(entity);
