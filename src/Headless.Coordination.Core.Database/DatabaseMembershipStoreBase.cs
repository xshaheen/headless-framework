// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Serializer;

namespace Headless.Coordination;

/// <summary>
/// Shared relational membership-store algorithm. Native providers own the SQL and clock expressions;
/// this base owns the cluster scoping and operation order.
/// </summary>
internal abstract class DatabaseMembershipStoreBase(CoordinationOptions options, IJsonSerializer serializer)
    : IMembershipStore
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

        return descriptor.Identity.NodeId.Value.Length == 0
            ? throw new ArgumentException("Descriptor identity must include a node id.", nameof(descriptor))
            : UpsertDescriptorCoreAsync(ClusterName, descriptor, cancellationToken);
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

    public ValueTask<NodeLivenessState?> ReadNodeLivenessAsync(
        NodeIdentity identity,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ReadCurrentNodeLivenessCoreAsync(ClusterName, identity, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<NodeIdentity>> ReadLiveNodesAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var identities = await ReadLiveNodesCoreAsync(ClusterName, cancellationToken).ConfigureAwait(false);

        // Order ascending by identity string for parity with ReadLivenessAsync, which the live set filters.
        return identities.OrderBy(static identity => identity.ToString(), StringComparer.Ordinal).ToArray();
    }

    /// <summary>Allocates the next durable incarnation for <paramref name="nodeId"/>.</summary>
    protected abstract ValueTask<NodeIncarnation> AllocateIncarnationCoreAsync(
        string clusterName,
        NodeId nodeId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Atomically writes the per-incarnation cold descriptor row (write-once) and an initial store-clock
    /// liveness row in the <c>Alive</c> state, both guarded by the current generation so a stale or
    /// impossible incarnation establishes neither. Runs inside a single guarded transaction.
    /// </summary>
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

    /// <summary>
    /// Reads the current-generation liveness state for a single identity, or <see langword="null"/> when the
    /// identity is absent from the current-generation snapshot view (not current, or at/beyond the retention
    /// window). Implementations must classify with the store clock identically to
    /// <see cref="ReadCurrentLivenessCoreAsync"/> and apply the retention boundary as a read-only cutoff —
    /// no pruning, no backfill.
    /// </summary>
    protected abstract ValueTask<NodeLivenessState?> ReadCurrentNodeLivenessCoreAsync(
        string clusterName,
        NodeIdentity identity,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Reads the current-generation <see cref="NodeLivenessState.Alive"/> node identities (identities only —
    /// no descriptor join). Implementations must join/filter against the generation authority and apply the
    /// store-clock alive window (not left, and beat age below the suspicion threshold); the base orders the
    /// result. Read-only: no prune, no backfill.
    /// </summary>
    protected abstract ValueTask<IReadOnlyList<NodeIdentity>> ReadLiveNodesCoreAsync(
        string clusterName,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Reads a single liveness snapshot from the canonical projection column layout shared by the relational
    /// providers: 0 = node id, 1 = incarnation, 2 = role (nullable), 3 = metadata json (nullable), 4 = state.
    /// </summary>
    protected async ValueTask<NodeLivenessSnapshot> ReadSnapshotAsync(
        DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        var nodeId = await reader.GetFieldValueAsync<string>(0, cancellationToken).ConfigureAwait(false);
        var incarnation = await reader.GetFieldValueAsync<long>(1, cancellationToken).ConfigureAwait(false);
        var identity = new NodeIdentity(new NodeId(nodeId), new NodeIncarnation(incarnation));

        var role = await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false)
            ? null
            : await reader.GetFieldValueAsync<string>(2, cancellationToken).ConfigureAwait(false);

        var metadataJson = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)
            ? "{}"
            : await reader.GetFieldValueAsync<string>(3, cancellationToken).ConfigureAwait(false);

        var stateText = await reader.GetFieldValueAsync<string>(4, cancellationToken).ConfigureAwait(false);
        var state = Enum.Parse<NodeLivenessState>(stateText);

        return new NodeLivenessSnapshot(identity, state, role, DeserializeDictionary(metadataJson));
    }

    protected string SerializeDictionary(IReadOnlyDictionary<string, string> value)
    {
        return serializer.SerializeToString(value) ?? "{}";
    }

    protected Dictionary<string, string> DeserializeDictionary(string value)
    {
        return serializer.Deserialize<Dictionary<string, string>>(value)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
