// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Store-authoritative membership and liveness contract for stamping work with <c>node@incarnation</c>.
/// </summary>
/// <remarks>
/// <para>
/// This contract is fencing-safe and fail-stop when backed by an eligible provider. It is <em>not</em> a
/// consensus system and must not be used as the sole mechanism for split-brain-proof leader election.
/// Membership events from <see cref="IMembershipEventSource.WatchAsync"/> are best-effort local
/// observations; callers must periodically reconcile from <see cref="GetLiveNodesAsync"/> or
/// <see cref="GetLivenessSnapshotAsync"/> and make recovery idempotent.
/// </para>
/// <para>
/// Each process registers once with <see cref="RegisterAsync"/> to obtain a <c>node@incarnation</c>
/// identity, then drives the heartbeat loop (typically via the internal background service). Failure to
/// heartbeat within <see cref="CoordinationOptions.DeadThreshold"/> causes the incarnation to transition
/// to <see cref="NodeLivenessState.Dead"/>, triggering dead-owner recovery for any resources the node
/// held. The process should then restart with a fresh incarnation.
/// </para>
/// </remarks>
[PublicAPI]
public interface INodeMembership : IMembershipEventSource
{
    /// <summary>
    /// The local <c>node@incarnation</c> identity acquired by <see cref="RegisterAsync"/>, or
    /// <see langword="null"/> if the node has not yet registered or has since left.
    /// </summary>
    NodeIdentity? Identity { get; }

    /// <summary>
    /// Cancelled when the local process's own membership is lost (superseded incarnation, store eviction,
    /// or explicit <see cref="LeaveAsync"/>). Use this token to stop ownership-sensitive background work
    /// without polling. The configured <see cref="CoordinationOptions.MembershipLostBehavior"/> determines
    /// whether the host is also stopped.
    /// </summary>
    CancellationToken LocalMembershipLostToken { get; }

    /// <summary>
    /// Registers this process as a cluster member and returns the allocated <c>node@incarnation</c>
    /// identity. The backing store atomically allocates a monotonically increasing incarnation number for
    /// the node id so that a restarted process always obtains a higher incarnation than any previous run.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The allocated <see cref="NodeIdentity"/> for this process.</returns>
    ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a heartbeat timestamp to the backing store, resetting the node's liveness clock.
    /// Called automatically by the internal heartbeat background service at
    /// <see cref="CoordinationOptions.HeartbeatInterval"/> intervals.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// <see langword="true"/> if the heartbeat was accepted by the store; <see langword="false"/> if the
    /// local identity has already been superseded and the node should stop.
    /// </returns>
    ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the local node as gracefully left and resets <see cref="Identity"/> to
    /// <see langword="null"/>. Emits a <see cref="NodeLeft"/> event to remote observers. After this call
    /// the node should not heartbeat or claim ownership of any resources.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    ValueTask LeaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the specified <c>node@incarnation</c> identity is currently
    /// <see cref="NodeLivenessState.Alive"/> according to the backing store.
    /// </summary>
    /// <param name="identity">The <c>node@incarnation</c> identity to test.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// <see langword="true"/> if the identity is classified as <see cref="NodeLivenessState.Alive"/>;
    /// <see langword="false"/> if it is suspected, dead, or unknown.
    /// </returns>
    ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the identities of all nodes currently classified as <see cref="NodeLivenessState.Alive"/>
    /// in the cluster.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A snapshot of live <c>node@incarnation</c> identities at the time of the call.</returns>
    ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full liveness snapshot for all known nodes, including suspected and dead incarnations
    /// retained within <see cref="CoordinationOptions.DeadRetentionWindow"/>.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// A point-in-time list of <see cref="NodeLivenessSnapshot"/> records for every tracked node
    /// incarnation. Use this to drive dead-owner recovery reconciliation.
    /// </returns>
    ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
        CancellationToken cancellationToken = default
    );
}
