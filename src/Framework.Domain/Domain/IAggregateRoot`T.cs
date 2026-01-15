// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Domain;

/// <summary>
/// Defines an aggregate root with a single primary key with "Id" property.
/// Used also to restrict repositories for example to work only with aggregate roots.
/// </summary>
/// <typeparam name="TId">Type of the primary key of the entity</typeparam>
public interface IAggregateRoot<out TId> : IEntity<TId>, IAggregateRoot;

/// <inheritdoc cref="IAggregateRoot{TId}"/>
public abstract class AggregateRoot<TId> : AggregateRoot, IAggregateRoot<TId>
    where TId : IEquatable<TId>
{
    /// <summary>Unique identifier for this entity.</summary>
    public required TId Id { get; init; }

    public override IReadOnlyList<object> GetKeys() => [Id];

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return Id;
    }

    public override string ToString() => $"[ENTITY: {GetType().Name}] Id = {Id}";
}
