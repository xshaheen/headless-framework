// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Marks an entity that carries an opaque concurrency stamp used for optimistic-concurrency checks.</summary>
/// <remarks>
/// The stamp is typically a random token (for example a GUID) refreshed on every write. Persistence layers
/// compare the stored stamp against the incoming value before committing, and reject writes where the values differ.
/// </remarks>
[PublicAPI]
public interface IHasConcurrencyStamp
{
    /// <summary>Opaque token that changes on every write; used by the persistence layer for optimistic-concurrency checks.</summary>
    string? ConcurrencyStamp { get; }
}
