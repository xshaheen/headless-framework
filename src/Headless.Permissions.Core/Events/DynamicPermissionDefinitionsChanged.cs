// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.Events;

/// <summary>
/// Integration message published via <c>IBus</c> whenever the dynamic permission definition store saves
/// new or updated permissions to the database. Consumers (e.g., other application instances or external
/// services) can subscribe to this message to react to definition changes in real time.
/// </summary>
public sealed class DynamicPermissionDefinitionsChanged
{
    /// <summary>
    /// A unique identifier for this event instance, generated with <c>IGuidGenerator</c>. Allows consumers
    /// to deduplicate deliveries in at-least-once messaging topologies.
    /// </summary>
    public required string UniqueId { get; init; }

    /// <summary>
    /// The names of the permissions that were added or updated in this save operation. Permissions that were
    /// only deleted are not included.
    /// </summary>
    public required HashSet<string> Permissions { get; init; }
}
