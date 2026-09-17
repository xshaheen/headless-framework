// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Defines an entity with a single primary key with "ID" property.</summary>
/// <typeparam name="TId">Type of the primary key of the entity</typeparam>
[PublicAPI]
public interface IEntity<out TId> : IEntity
    where TId : IEquatable<TId> // The 'notnull' constraint is redundant because type parameter 'TId' is constrained by non-nullable type 'IEquatable<TId>'
{
    /// <summary>Unique identifier for this entity.</summary>
    TId Id { get; }
}

/// <summary>Base class for entities with a single primary key.</summary>
[PublicAPI]
public abstract class Entity<TId> : Entity, IEntity<TId>
    where TId : IEquatable<TId> // The 'notnull' constraint is redundant because type parameter 'TId' is constrained by non-nullable type 'IEquatable<TId>'
{
    /// <summary>Initializes a new instance of the <see cref="Entity{TId}"/> class.</summary>
    protected Entity() { }

    /// <summary>Initializes a new instance of the <see cref="Entity{TId}"/> class with the specified identifier.</summary>
    /// <param name="id">The unique identifier for this entity.</param>
    [SetsRequiredMembers]
    protected Entity(TId id)
    {
        Id = id;
    }

    /// <summary>Unique identifier for this entity.</summary>
    public required TId Id { get; init; }

    /// <inheritdoc/>
    public override IReadOnlyList<object> GetKeys()
    {
        return [Id];
    }

    /// <inheritdoc/>
    protected override bool EqualityComponentsEqual(Entity other)
    {
        return Id.Equals(((Entity<TId>)other).Id);
    }

    /// <inheritdoc/>
    protected override void BuildHashCode(ref HashCode hash)
    {
        hash.Add(Id);
    }

    /// <summary>Returns a diagnostic string of the form <c>[ENTITY: TypeName] Id = &lt;id&gt;</c>.</summary>
    public override string ToString()
    {
        return $"[ENTITY: {GetType().Name}] Id = {Id}";
    }
}
