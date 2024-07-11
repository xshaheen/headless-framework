namespace Framework.BuildingBlocks.Domains;

/// <summary>Defines an entity. It's primary key may not be "Id" or it may have a composite primary key.</summary>
public interface IEntity
{
    /// <summary>Returns an array of ordered keys for this entity.</summary>
    IReadOnlyList<object> GetKeys();
}

/// <inheritdoc cref="IEntity"/>
public abstract class Entity : Base<Entity>, IEntity
{
    public abstract IReadOnlyList<object> GetKeys();

    protected override IEnumerable<object?> EqualityComponents() => GetKeys();

    public override string ToString() => $"[ENTITY: {GetType().Name}] Keys = {string.Join(", ", GetKeys())}";
}
