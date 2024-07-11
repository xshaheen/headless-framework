#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks;

/// <summary>An exception that is thrown if can not find a entity in database.</summary>
public sealed class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string entity, Guid key)
        : base(_BuildMessage(entity, key.ToString()))
    {
        (Entity, Key) = (entity, key.ToString());
    }

    public EntityNotFoundException(string entity, string key)
        : base(_BuildMessage(entity, key))
    {
        (Entity, Key) = (entity, key);
    }

    public EntityNotFoundException(string entity, int key)
        : base(_BuildMessage(entity, key.ToString(CultureInfo.InvariantCulture)))
    {
        (Entity, Key) = (entity, key.ToString(CultureInfo.InvariantCulture));
    }

    public EntityNotFoundException(string entity, long key)
        : base(_BuildMessage(entity, key.ToString(CultureInfo.InvariantCulture)))
    {
        (Entity, Key) = (entity, key.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Entity name.</summary>
    public string Entity { get; }

    /// <summary>Search value.</summary>
    public string Key { get; }

    private static string _BuildMessage(string entity, string key)
    {
        return $"No entity founded - [{entity}:{key}]";
    }
}
