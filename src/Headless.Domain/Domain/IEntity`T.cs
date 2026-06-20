// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Defines an entity with a single primary key with "ID" property.</summary>
/// <typeparam name="TId">Type of the primary key of the entity</typeparam>
[PublicAPI]
public interface IEntity<out TId> : IEntity
    where TId : notnull
{
    /// <summary>Unique identifier for this entity.</summary>
    TId Id { get; }
}

/// <summary>Base class for entities with a single primary key.</summary>
[PublicAPI]
public abstract class Entity<TId> : Entity, IEntity<TId>
    where TId : notnull, IEquatable<TId>
{
    /// <summary>Unique identifier for this entity.</summary>
    public required TId Id { get; init; }

    public override IReadOnlyList<object> GetKeys() => [Id];

    public override string ToString() => $"[ENTITY: {GetType().Name}] Id = {Id}";
}
