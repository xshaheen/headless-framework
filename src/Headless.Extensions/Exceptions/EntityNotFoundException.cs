// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Exceptions;

/// <summary>An exception that is thrown when we can not find an entity in database.</summary>
[PublicAPI]
public sealed class EntityNotFoundException : Exception
{
    /// <summary>Initializes the exception for an entity identified by a <see cref="Guid"/> key.</summary>
    /// <param name="entity">The name of the entity that was not found.</param>
    /// <param name="key">The key value that was searched for.</param>
    public EntityNotFoundException(string entity, Guid key)
        : base(_BuildMessage(entity, key.ToString()))
    {
        (Entity, Key) = (entity, key.ToString());
    }

    /// <summary>Initializes the exception for an entity identified by a string key.</summary>
    /// <param name="entity">The name of the entity that was not found.</param>
    /// <param name="key">The key value that was searched for.</param>
    public EntityNotFoundException(string entity, string key)
        : base(_BuildMessage(entity, key))
    {
        (Entity, Key) = (entity, key);
    }

    /// <summary>Initializes the exception for an entity identified by an <see cref="int"/> key.</summary>
    /// <param name="entity">The name of the entity that was not found.</param>
    /// <param name="key">The key value that was searched for.</param>
    public EntityNotFoundException(string entity, int key)
        : base(_BuildMessage(entity, key.ToString(CultureInfo.InvariantCulture)))
    {
        (Entity, Key) = (entity, key.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Initializes the exception for an entity identified by a <see cref="long"/> key.</summary>
    /// <param name="entity">The name of the entity that was not found.</param>
    /// <param name="key">The key value that was searched for.</param>
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
