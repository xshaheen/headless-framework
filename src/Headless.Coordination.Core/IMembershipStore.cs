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

    /// <summary>
    /// Reads the liveness state of a single <paramref name="identity"/> without reading any other node — the
    /// targeted, bounded counterpart to <see cref="ReadLivenessAsync"/> for per-request liveness checks.
    /// </summary>
    /// <remarks>
    /// The result MUST equal what <see cref="ReadLivenessAsync"/> would conclude for the same identity at the
    /// same instant: it is current-generation-only (a stale, superseded, or never-registered incarnation is
    /// not surfaced), it classifies with the store clock using the same suspicion/dead thresholds, and it
    /// honors the same retention boundary — a row at or beyond the retention window reads as absent, matching
    /// the snapshot read which produces absence by pruning. A return of <see langword="null"/> means the
    /// identity is absent from the current-generation snapshot view; it deliberately collapses the absence
    /// sub-states (never-registered / stale / superseded / retention-expired). Distinguishing those is an
    /// additive extension point, not modeled here. Implementations MUST be read-only — no pruning, no
    /// generation-mirror backfill — so the call stays a bounded single-row/single-member operation.
    /// </remarks>
    ValueTask<NodeLivenessState?> ReadNodeLivenessAsync(
        NodeIdentity identity,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the identities of all nodes currently classified as <see cref="NodeLivenessState.Alive"/> in
    /// the cluster, ordered ascending by the <c>node@incarnation</c> string — the targeted counterpart to
    /// filtering <see cref="ReadLivenessAsync"/> down to the live set.
    /// </summary>
    /// <remarks>
    /// The result MUST equal the set of <see cref="NodeLivenessState.Alive"/> identities
    /// <see cref="ReadLivenessAsync"/> would yield at the same instant: current-generation only (a superseded
    /// incarnation is excluded even while still within its alive window), store-clock classified with the same
    /// suspicion threshold, and ordered ascending by identity. Implementations MAY serve it from a dedicated
    /// live-node index (the Redis <c>:live</c> sorted set) instead of materializing the full snapshot, and
    /// MUST be read-only — no prune, no backfill.
    /// </remarks>
    ValueTask<IReadOnlyList<NodeIdentity>> ReadLiveNodesAsync(CancellationToken cancellationToken = default);
}
