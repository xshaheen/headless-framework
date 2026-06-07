// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Provider SPI for store-authoritative membership operations.</summary>
public interface IMembershipStore
{
    ValueTask<NodeIncarnation> AllocateIncarnationAsync(NodeId nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Durably establishes the register-time state for the descriptor's freshly-allocated incarnation:
    /// the write-once cold descriptor plus an initial store-clock liveness entry in the <c>Alive</c> state,
    /// guarded by the current generation. This makes a node live immediately after register, independent of
    /// the heartbeat loop. A stale or impossible incarnation must not establish or overwrite liveness.
    /// </summary>
    ValueTask UpsertDescriptorAsync(NodeDescriptor descriptor, CancellationToken cancellationToken = default);

    ValueTask<bool> HeartbeatAsync(NodeIdentity identity, CancellationToken cancellationToken = default);

    ValueTask LeaveAsync(NodeIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>Returns a snapshot of every known node's liveness state.</summary>
    /// <remarks>
    /// Results MUST be ordered by <see cref="NodeLivenessSnapshot.Identity"/> ascending. The service layer
    /// consumes this directly and does not re-sort, so an unsorted implementation produces non-deterministic
    /// event ordering and snapshot output. Provider conformance tests must assert this ordering.
    /// </remarks>
    ValueTask<IReadOnlyList<NodeLivenessSnapshot>> ReadLivenessAsync(CancellationToken cancellationToken = default);
}
