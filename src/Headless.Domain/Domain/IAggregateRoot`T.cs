// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Defines an aggregate root with a single primary key with "Id" property.
/// Used also to restrict repositories for example to work only with aggregate roots.
/// </summary>
/// <typeparam name="TId">Type of the primary key of the entity</typeparam>
[PublicAPI]
public interface IAggregateRoot<out TId> : IEntity<TId>, IAggregateRoot
    where TId : notnull;

/// <summary>Base class for aggregate roots with a single primary key.</summary>
[PublicAPI]
public abstract class AggregateRoot<TId> : AggregateRoot, IAggregateRoot<TId>
    where TId : notnull, IEquatable<TId>
{
    /// <summary>Unique identifier for this entity.</summary>
    public required TId Id { get; init; }

    /// <inheritdoc/>
    public override IReadOnlyList<object> GetKeys() => [Id];

    /// <inheritdoc/>
    protected override bool EqualityComponentsEqual(Entity other) => Id.Equals(((AggregateRoot<TId>)other).Id);

    /// <inheritdoc/>
    protected override void BuildHashCode(ref HashCode hash) => hash.Add(Id);

    /// <summary>Returns a diagnostic string of the form <c>[ENTITY: TypeName] Id = &lt;id&gt;</c>.</summary>
    public override string ToString() => $"[ENTITY: {GetType().Name}] Id = {Id}";
}
