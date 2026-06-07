// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Store-authoritative membership and liveness contract for stamping work with <c>node@incarnation</c>.
/// </summary>
/// <remarks>
/// This contract is fencing-safe, fail-stop, and fail-closed when backed by an eligible provider. It is
/// not a consensus system and must not be used as the sole mechanism for split-brain-proof leadership.
/// Consumers must treat membership events as best-effort acceleration and periodically reconcile from
/// authoritative snapshots.
/// </remarks>
[PublicAPI]
public interface INodeMembership : IMembershipEventSource
{
    NodeIdentity? Identity { get; }

    CancellationToken LocalMembershipLostToken { get; }

    ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default);

    ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default);

    ValueTask LeaveAsync(CancellationToken cancellationToken = default);

    ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
        CancellationToken cancellationToken = default
    );
}
