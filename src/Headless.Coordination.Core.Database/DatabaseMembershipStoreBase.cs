// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Text.Json;

namespace Headless.Coordination;

/// <summary>
/// Shared relational membership-store algorithm. Native providers own the SQL and clock expressions;
/// this base owns the cluster scoping and operation order.
/// </summary>
internal abstract class DatabaseMembershipStoreBase(CoordinationOptions options) : IMembershipStore
{
    private static readonly JsonSerializerOptions _JsonOptions = new(JsonSerializerDefaults.Web);

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
            ? throw new ArgumentException(@"Descriptor identity must include a node id.", nameof(descriptor))
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
    /// Reads a single liveness snapshot from the canonical projection column layout shared by the relational
    /// providers: 0 = node id, 1 = incarnation, 2 = role (nullable), 3 = metadata json (nullable), 4 = state.
    /// </summary>
    protected static NodeLivenessSnapshot ReadSnapshot(DbDataReader reader)
    {
        var identity = new NodeIdentity(new NodeId(reader.GetString(0)), new NodeIncarnation(reader.GetInt64(1)));
        var role = reader.IsDBNull(2) ? null : reader.GetString(2);
        var metadataJson = reader.IsDBNull(3) ? "{}" : reader.GetString(3);
        var state = Enum.Parse<NodeLivenessState>(reader.GetString(4));

        return new NodeLivenessSnapshot(identity, state, role, DeserializeDictionary(metadataJson));
    }

    protected static string SerializeDictionary(IReadOnlyDictionary<string, string> value)
    {
        return JsonSerializer.Serialize(value, _JsonOptions);
    }

    protected static Dictionary<string, string> DeserializeDictionary(string value)
    {
        return JsonSerializer.Deserialize<Dictionary<string, string>>(value, _JsonOptions)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
