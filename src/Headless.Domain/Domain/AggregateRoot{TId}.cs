// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>Base class for aggregate roots with a single primary key.</summary>
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
