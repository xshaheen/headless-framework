// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>
/// Defines an aggregate root with a single primary key with "Id" property.
/// Used also to restrict repositories for example to work only with aggregate roots.
/// </summary>
/// <typeparam name="TId">Type of the primary key of the entity</typeparam>
[PublicAPI]
public interface IAggregateRoot<out TId> : IEntity<TId>, IAggregateRoot
    where TId : IEquatable<TId>; // The 'notnull' constraint is redundant because type parameter 'TId' is constrained by non-nullable type 'IEquatable<TId>'
