// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless;

/// <summary>An exception thrown when a requested entity cannot be found in the data store.</summary>
/// <remarks>
/// Derive from this class to give a missing entity its own type that application code can catch by name. The API
/// exception handler maps every derived type to the same 404 body as <see cref="EntityNotFoundException"/>, which never
/// carries the entity name, the key, or the message, so the derived type needs no exception handler of its own.
/// </remarks>
[PublicAPI]
public class EntityNotFoundException : Exception
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

    /// <summary>Initializes a derived exception with its own message.</summary>
    /// <param name="message">The exception message, for logs; it never reaches the response.</param>
    /// <param name="entity">The name of the entity that was not found.</param>
    /// <param name="key">The key value that was searched for.</param>
    /// <param name="innerException">The exception that caused this failure, if any.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="message"/>, <paramref name="entity"/>, or <paramref name="key"/> is <see langword="null"/>.
    /// </exception>
    protected EntityNotFoundException(string message, string entity, string key, Exception? innerException = null)
        : base(Argument.IsNotNull(message), innerException)
    {
        (Entity, Key) = (Argument.IsNotNull(entity), Argument.IsNotNull(key));
    }

    /// <summary>The name of the entity type that was not found.</summary>
    public string Entity { get; }

    /// <summary>The key value that was searched for.</summary>
    public string Key { get; }

    private static string _BuildMessage(string entity, string key)
    {
        return $"No entity founded - [{entity}:{key}]";
    }
}
