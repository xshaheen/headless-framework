namespace Framework.BuildingBlocks.Domains.Events;

public class EntityEventData<TEntity>(TEntity entity)
{
    /// <summary>Related entity with this event.</summary>
    public TEntity Entity { get; } = entity;
}
