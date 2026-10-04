// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>
/// Defines an aggregate root with a strongly typed primary key named <c>Id</c>.
/// </summary>
/// <remarks>
/// Used to restrict repositories and persistence operations to aggregate roots.
/// </remarks>
/// <typeparam name="TId">The primary key type.</typeparam>
[PublicAPI]
public interface IAggregateRoot<out TId> : IEntity<TId>, IAggregateRoot
    where TId : IEquatable<TId>; // The 'notnull' constraint is redundant because type parameter 'TId' is constrained by non-nullable type 'IEquatable<TId>'

/// <summary>Provides a base implementation for aggregate roots with a single primary key.</summary>
/// <typeparam name="TId">The primary key type.</typeparam>
[PublicAPI]
public abstract class AggregateRoot<TId> : AggregateRoot, IAggregateRoot<TId>
    where TId : IEquatable<TId>
{
    /// <summary>Initializes a new instance of the <see cref="AggregateRoot{TId}"/> class.</summary>
    protected AggregateRoot() { }

    /// <summary>Initializes a new instance of the <see cref="AggregateRoot{TId}"/> class with the specified identifier.</summary>
    /// <param name="id">The unique identifier for this entity.</param>
    [SetsRequiredMembers]
    protected AggregateRoot(TId id)
    {
        Id = id;
    }

    /// <summary>Gets the unique identifier for this entity.</summary>
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
