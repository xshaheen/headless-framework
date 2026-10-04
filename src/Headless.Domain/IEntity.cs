// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>Defines an entity.</summary>
/// <remarks>The entity primary key may be non-standard or composite.</remarks>
[PublicAPI]
public interface IEntity
{
    /// <summary>Returns an ordered list of keys for this entity.</summary>
    /// <returns>A read-only list of keys.</returns>
    IReadOnlyList<object> GetKeys();

    /// <summary>Returns a colon-delimited composite key string built from <see cref="GetKeys"/>.</summary>
    /// <returns>The combined key string.</returns>
    string GetKey() => string.Join(':', GetKeys());
}

/// <summary>Provides a base implementation for entities that compare equality by ordered keys.</summary>
[PublicAPI]
public abstract class Entity : EqualityBase<Entity>, IEntity
{
    /// <inheritdoc/>
    public abstract IReadOnlyList<object> GetKeys();

    /// <inheritdoc/>
    protected override bool EqualityComponentsEqual(Entity other)
    {
        var keys = GetKeys();
        var otherKeys = other.GetKeys();

        if (keys.Count != otherKeys.Count)
        {
            return false;
        }

        for (var i = 0; i < keys.Count; i++)
        {
            if (!Equals(keys[i], otherKeys[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    protected override void BuildHashCode(ref HashCode hash)
    {
        var keys = GetKeys();

        for (var i = 0; i < keys.Count; i++)
        {
            hash.Add(keys[i]);
        }
    }

    /// <summary>Returns a diagnostic string of the form <c>[ENTITY: TypeName] Keys = k1, k2, ...</c>.</summary>
    public override string ToString()
    {
        return $"[ENTITY: {GetType().Name}] Keys = {string.Join(", ", GetKeys())}";
    }
}
