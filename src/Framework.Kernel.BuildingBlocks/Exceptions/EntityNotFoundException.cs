// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

/// <summary>An exception that is thrown if can not find an entity in database.</summary>
[PublicAPI]
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
