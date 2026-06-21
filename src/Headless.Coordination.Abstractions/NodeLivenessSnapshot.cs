// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Point-in-time liveness record for a single node incarnation, returned by
/// <see cref="INodeMembership.GetLivenessSnapshotAsync"/>.
/// </summary>
/// <param name="Identity">The <c>node@incarnation</c> identity this snapshot describes.</param>
/// <param name="State">The liveness state computed from the last heartbeat timestamp.</param>
/// <param name="Role">The role label registered by the node, or <see langword="null"/> if not set.</param>
/// <param name="Metadata">Metadata dictionary registered by the node at join time.</param>
[PublicAPI]
public sealed record NodeLivenessSnapshot(
    NodeIdentity Identity,
    NodeLivenessState State,
    string? Role,
    IReadOnlyDictionary<string, string> Metadata
);
