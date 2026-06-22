// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Marker interface for integration events — cross-service, distributed messages published by an aggregate
/// after a transaction commits and consumed by other bounded contexts or external systems.
/// </summary>
/// <remarks>
/// Integration events cross process boundaries and are typically delivered via a message broker.
/// They differ from domain events, which are in-process and dispatched within the same unit of work.
/// </remarks>
[PublicAPI]
public interface IIntegrationEvent
{
    /// <summary>
    /// Globally unique identifier for this event instance.
    /// Implementations must assign a value that is unique per raised event so downstream consumers
    /// can perform idempotency checks and deduplication.
    /// </summary>
    string UniqueId { get; }
}
