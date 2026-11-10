// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Represents a point-in-time liveness record for a single node incarnation, returned by
/// <see cref="INodeMembership.GetLivenessSnapshotAsync"/>.
/// </summary>
/// <param name="Identity">The node and incarnation identity this snapshot describes.</param>
/// <param name="State">The liveness state computed from the last heartbeat timestamp.</param>
/// <param name="Role">The role label registered by the node, or <see langword="null"/> if not set.</param>
/// <param name="Metadata">Metadata dictionary registered by the node at join time.</param>
/// <param name="LastHeartbeatAt">
/// The store-clock instant of the incarnation's latest accepted heartbeat or registration: the value
/// <paramref name="State"/> is computed from. After a graceful leave the relational stores keep the last real beat,
/// while Redis marks the leave by backdating it one dead threshold, so on Redis a left node does not report its true
/// last beat.
/// </param>
[PublicAPI]
public sealed record NodeLivenessSnapshot(
    NodeIdentity Identity,
    NodeLivenessState State,
    string? Role,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset LastHeartbeatAt
);
