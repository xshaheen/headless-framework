// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Marker interface for in-process domain events raised by aggregates and dispatched
/// within the current unit of work via <c>ILocalEventBus</c>.
/// </summary>
[PublicAPI]
public interface IDomainEvent
{
    /// <summary>Globally unique identifier for this event instance. Implementations must assign a value that is
    /// unique per raised event (for example a GUID); downstream dispatch/outbox layers use it for deduplication.</summary>
    string UniqueId { get; }
}
