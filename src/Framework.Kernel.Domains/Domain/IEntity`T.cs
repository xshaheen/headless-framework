// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

/// <summary>Defines an entity with a single primary key with "Id" property.</summary>
/// <typeparam name="TId">Type of the primary key of the entity</typeparam>
public interface IEntity<out TId> : IEntity
{
    /// <summary>Unique identifier for this entity.</summary>
    public TId Id { get; }
}

/// <inheritdoc cref="IEntity{TId}"/>
public abstract class Entity<TId> : Entity, IEntity<TId>
    where TId : IEquatable<TId>
{
    /// <summary>Unique identifier for this entity.</summary>
    public virtual TId Id { get; protected init; } = default!;

    public override IReadOnlyList<object> GetKeys() => [Id];

    public override string ToString() => $"[ENTITY: {GetType().Name}] Id = {Id}";
}
