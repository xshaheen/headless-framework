// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

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
