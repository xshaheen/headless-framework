// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Defines an entity. It's primary key may not be "ID" or it may have a composite primary key.</summary>
[PublicAPI]
public interface IEntity
{
    /// <summary>Returns an array of ordered keys for this entity.</summary>
    IReadOnlyList<object> GetKeys();

    /// <summary>Returns a colon-delimited composite key string built from <c>GetKeys()</c>.</summary>
    string GetKey() => string.Join(':', GetKeys());
}

/// <summary>Base class for entities that compare equality by their ordered keys.</summary>
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
    public override string ToString() => $"[ENTITY: {GetType().Name}] Keys = {string.Join(", ", GetKeys())}";
}
