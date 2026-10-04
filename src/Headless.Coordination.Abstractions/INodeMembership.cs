// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Provides store-authoritative membership and liveness tracking for stamping work with
/// <c>node@incarnation</c> identities.
/// </summary>
/// <remarks>
/// <para>
/// This contract supports fencing and fail-stop semantics when backed by an eligible provider.
/// It is not a consensus protocol and must not serve as the sole mechanism for split-brain-proof
/// leader election. Membership events from <see cref="IMembershipEventSource.WatchAsync"/> are best-effort
/// local observations. Callers must periodically reconcile state using <see cref="GetLiveNodesAsync"/> or
/// <see cref="GetLivenessSnapshotAsync"/> and ensure recovery logic is idempotent.
/// </para>
/// <para>
/// Each process registers once using <see cref="RegisterAsync"/> to obtain a node and incarnation identity,
/// then runs the heartbeat loop, typically through the internal heartbeat background service. Failing to
/// heartbeat within <see cref="CoordinationOptions.DeadThreshold"/> transitions the incarnation to
/// <see cref="NodeLivenessState.Dead"/>, triggering dead-owner recovery for held resources. The process
/// must then restart with a fresh incarnation.
/// </para>
/// </remarks>
[PublicAPI]
public interface INodeMembership : IMembershipEventSource
{
    /// <summary>
    /// Gets the local node and incarnation identity acquired by <see cref="RegisterAsync"/>,
    /// or <see langword="null"/> if the node has not registered or has left.
    /// </summary>
    NodeIdentity? Identity { get; }

    /// <summary>
    /// Gets a cancellation token cancelled when the local membership is lost: through a superseded
    /// incarnation, store eviction, explicit call to <see cref="LeaveAsync"/>, or a heartbeat write that
    /// could not be confirmed within the remaining <see cref="CoordinationOptions.DeadThreshold"/> budget.
    /// A hung or continuously failing store call therefore self-fences the node even when the store never
    /// rejects the write. Use this token to stop ownership-sensitive background work without polling.
    /// The configured <see cref="CoordinationOptions.MembershipLostBehavior"/> determines whether the host
    /// process also stops.
    /// </summary>
    CancellationToken LocalMembershipLostToken { get; }

    /// <summary>
    /// Registers this process as a cluster member and returns the allocated node and incarnation identity.
    /// The backing store atomically allocates a monotonically increasing incarnation counter for the node
    /// identifier, so a restarted process always obtains a higher incarnation than any previous run.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The allocated <see cref="NodeIdentity"/> for this process.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is already cancelled.</exception>
    ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a heartbeat timestamp to the backing store, resetting the liveness timer of the node.
    /// The internal heartbeat background service calls this automatically at
    /// <see cref="CoordinationOptions.HeartbeatInterval"/> intervals.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> if the heartbeat was accepted by the store; <see langword="false"/> if the
    /// local identity has been superseded, declared dead, left, or pruned, and the node should stop.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is already cancelled.</exception>
    ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the local node as gracefully left and clears <see cref="Identity"/>.
    /// Emits a <see cref="NodeLeft"/> event to observers. After this call, the node must not heartbeat
    /// or claim ownership of any resources.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A value task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is already cancelled.</exception>
    ValueTask LeaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the specified identity is classified as <see cref="NodeLivenessState.Alive"/> by the store.
    /// </summary>
    /// <param name="identity">The node and incarnation identity to check.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> if the identity is classified as <see cref="NodeLivenessState.Alive"/>;
    /// <see langword="false"/> if it is suspected, dead, or unknown.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is already cancelled.</exception>
    ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the identities of all nodes classified as <see cref="NodeLivenessState.Alive"/> in the cluster.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A snapshot of the live node and incarnation identities at the time of the call.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is already cancelled.</exception>
    ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full liveness snapshot for all known nodes, including suspected and dead incarnations
    /// still within the provider's retention window.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A point-in-time list of <see cref="NodeLivenessSnapshot"/> records for every tracked node
    /// incarnation. Use this list to drive dead-owner recovery reconciliation.
    /// </returns>
    /// <remarks>
    /// <see cref="CoordinationOptions.DeadRetentionWindow"/> is the minimum time a provider retains a dead
    /// incarnation, not a ceiling. Relational providers prune shortly after
    /// <see cref="CoordinationOptions.DeadThreshold"/>, while the Redis store retains records for its
    /// <c>RedisKnownNodeRetention</c> (7 days by default). Classify by <see cref="NodeLivenessState"/>
    /// rather than assuming records expire immediately after
    /// <see cref="CoordinationOptions.DeadRetentionWindow"/>.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is already cancelled.</exception>
    ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
        CancellationToken cancellationToken = default
    );
}
