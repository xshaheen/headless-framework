// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Defines an entity. It's primary key may not be "ID" or it may have a composite primary key.</summary>
public interface IEntity
{
    /// <summary>Returns an array of ordered keys for this entity.</summary>
    IReadOnlyList<object> GetKeys();

    string GetKey() => string.Join(':', GetKeys());
}

/// <summary>Base class for entities that compare equality by their ordered keys.</summary>
public abstract class Entity : EqualityBase<Entity>, IEntity
{
    public abstract IReadOnlyList<object> GetKeys();

    protected override IEnumerable<object?> EqualityComponents() => GetKeys();

    public override string ToString() => $"[ENTITY: {GetType().Name}] Keys = {string.Join(", ", GetKeys())}";
}
