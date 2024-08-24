namespace Framework.BuildingBlocks.Domains.Events;

public abstract class EntityEventData<TEntity>(TEntity entity) : ILocalMessage
{
    /// <summary>Related entity with this event.</summary>
    public TEntity Entity { get; } = entity;
}
