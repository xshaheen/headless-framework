// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Provides store-authoritative membership and liveness tracking using node and incarnation identities.
/// </summary>
/// <remarks>
/// <para>
/// This contract supports fencing and fail-stop semantics when backed by an eligible provider.
/// It is not a consensus protocol and must not serve as the sole mechanism for leader election.
/// Membership events from <see cref="IMembershipEventSource.WatchAsync"/> are best-effort local observations.
/// Callers must periodically reconcile state using <see cref="GetLiveNodesAsync"/> or <see cref="GetLivenessSnapshotAsync"/>
/// and ensure recovery logic is idempotent.
/// </para>
/// <para>
/// Each process registers once using <see cref="RegisterAsync"/> to obtain a node and incarnation identity,
/// then runs the heartbeat loop. Failing to heartbeat within <see cref="CoordinationOptions.DeadThreshold"/>
/// transitions the incarnation to <see cref="NodeLivenessState.Dead"/>, triggering dead-owner recovery for held resources.
/// The process must restart to obtain a fresh incarnation.
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
    /// Gets a cancellation token cancelled when the local membership is lost through a superseded incarnation,
    /// store eviction, explicit call to <see cref="LeaveAsync"/>, or heartbeat timeout.
    /// Use this token to stop ownership-sensitive background work. The configured
    /// <see cref="CoordinationOptions.MembershipLostBehavior"/> determines whether the host process also stops.
    /// </summary>
    CancellationToken LocalMembershipLostToken { get; }

    /// <summary>
    /// Registers this process as a cluster member and returns the allocated node and incarnation identity.
    /// The backing store atomically allocates a monotonically increasing incarnation counter for the node identifier.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The allocated <see cref="NodeIdentity"/> for this process.</returns>
    ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a heartbeat timestamp to the backing store, resetting the liveness timer of the node.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> if the heartbeat was accepted by the store; <see langword="false"/> if the
    /// local identity has been superseded, declared dead, left, or pruned.
    /// </returns>
    ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the local node as left and clears <see cref="Identity"/>.
    /// Emits a <see cref="NodeLeft"/> event to observers.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A value task that represents the asynchronous operation.</returns>
    ValueTask LeaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the specified identity is classified as <see cref="NodeLivenessState.Alive"/> by the store.
    /// </summary>
    /// <param name="identity">The node and incarnation identity to check.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> if the identity is classified as <see cref="NodeLivenessState.Alive"/>;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the identities of all nodes classified as <see cref="NodeLivenessState.Alive"/> in the cluster.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of live node and incarnation identities.</returns>
    ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full liveness snapshot for all known nodes, including suspected and dead incarnations.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A point-in-time list of <see cref="NodeLivenessSnapshot"/> records for all tracked node incarnations.
    /// </returns>
    /// <remarks>
    /// <see cref="CoordinationOptions.DeadRetentionWindow"/> represents the minimum time a dead incarnation
    /// is retained by the provider. Relational providers prune shortly after <see cref="CoordinationOptions.DeadThreshold"/>,
    /// while Redis retains records for a configured duration. Classify by <see cref="NodeLivenessState"/> rather than assuming
    /// records expire immediately after <see cref="CoordinationOptions.DeadRetentionWindow"/>.
    /// </remarks>
    ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
        CancellationToken cancellationToken = default
    );
}
