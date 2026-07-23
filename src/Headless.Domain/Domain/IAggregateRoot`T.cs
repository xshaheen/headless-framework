// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Defines an aggregate root with a single primary key with "Id" property.
/// Used also to restrict repositories for example to work only with aggregate roots.
/// </summary>
/// <typeparam name="TId">Type of the primary key of the entity</typeparam>
[PublicAPI]
public interface IAggregateRoot<out TId> : IEntity<TId>, IAggregateRoot
    where TId : IEquatable<TId>; // The 'notnull' constraint is redundant because type parameter 'TId' is constrained by non-nullable type 'IEquatable<TId>'

/// <summary>Base class for aggregate roots with a single primary key.</summary>
[PublicAPI]
public abstract class AggregateRoot<TId> : AggregateRoot, IAggregateRoot<TId>
    where TId : IEquatable<TId>
{
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
        return Id.Equals(((AggregateRoot<TId>)other).Id);
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
