// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Shared relational membership-store algorithm. Native providers own the SQL and clock expressions;
/// this base owns the cluster scoping and operation order.
/// </summary>
internal abstract class DatabaseMembershipStoreBase(CoordinationOptions options) : IMembershipStore
{
    protected string ClusterName => options.ClusterName;

    protected TimeSpan SuspicionThreshold => options.SuspicionThreshold;

    protected TimeSpan DeadThreshold => options.DeadThreshold;

    protected TimeSpan DeadRetentionWindow => options.DeadRetentionWindow;

    public ValueTask<NodeIncarnation> AllocateIncarnationAsync(
        NodeId nodeId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return AllocateIncarnationCoreAsync(ClusterName, nodeId, cancellationToken);
    }

    public ValueTask UpsertDescriptorAsync(NodeDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (descriptor.Identity.NodeId.Value.Length == 0)
        {
            throw new ArgumentException("Descriptor identity must include a node id.", nameof(descriptor));
        }

        return UpsertDescriptorCoreAsync(ClusterName, descriptor, cancellationToken);
    }

    public ValueTask<bool> HeartbeatAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return HeartbeatCoreAsync(ClusterName, identity, cancellationToken);
    }

    public ValueTask LeaveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return LeaveCoreAsync(ClusterName, identity, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<NodeLivenessSnapshot>> ReadLivenessAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshots = await ReadCurrentLivenessCoreAsync(ClusterName, cancellationToken).ConfigureAwait(false);

        return snapshots.OrderBy(static snapshot => snapshot.Identity.ToString(), StringComparer.Ordinal).ToArray();
    }

    /// <summary>Allocates the next durable incarnation for <paramref name="nodeId"/>.</summary>
    protected abstract ValueTask<NodeIncarnation> AllocateIncarnationCoreAsync(
        string clusterName,
        NodeId nodeId,
        CancellationToken cancellationToken
    );

    /// <summary>Writes the per-incarnation cold descriptor row. Implementations keep it immutable.</summary>
    protected abstract ValueTask UpsertDescriptorCoreAsync(
        string clusterName,
        NodeDescriptor descriptor,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Writes a store-clock heartbeat only when <paramref name="identity"/>'s incarnation equals the
    /// durable current generation for the same cluster and node id.
    /// </summary>
    protected abstract ValueTask<bool> HeartbeatCoreAsync(
        string clusterName,
        NodeIdentity identity,
        CancellationToken cancellationToken
    );

    /// <summary>Marks only the supplied node incarnation as left/dead.</summary>
    protected abstract ValueTask LeaveCoreAsync(
        string clusterName,
        NodeIdentity identity,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Reads the current-generation operational liveness projection. Implementations must join/filter
    /// against the generation authority and classify Alive/Suspected/Dead with the store clock.
    /// </summary>
    protected abstract ValueTask<IReadOnlyList<NodeLivenessSnapshot>> ReadCurrentLivenessCoreAsync(
        string clusterName,
        CancellationToken cancellationToken
    );
}
