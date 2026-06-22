// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
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
    protected override IEnumerable<object?> EqualityComponents() => GetKeys();

    /// <summary>Returns a diagnostic string of the form <c>[ENTITY: TypeName] Keys = k1, k2, ...</c>.</summary>
    public override string ToString() => $"[ENTITY: {GetType().Name}] Keys = {string.Join(", ", GetKeys())}";
}
