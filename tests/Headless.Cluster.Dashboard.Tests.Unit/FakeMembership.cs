// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Coordination;

namespace Tests;

/// <summary>
/// A membership store a test sets by hand: its liveness snapshot, this node's event log, and a switch that makes the
/// snapshot read fail as a store that does not answer.
/// </summary>
internal sealed class FakeMembership : INodeMembership, IMembershipEventLog
{
    private readonly Lock _lock = new();
    private IReadOnlyList<NodeLivenessSnapshot> _snapshot = [];
    private IReadOnlyList<MembershipEventLogEntry> _entries = [];

    public NodeIdentity? Identity { get; set; }

    public string ClusterName { get; set; } = "orders";

    public bool IsUnreachable { get; set; }

    public int SnapshotReads { get; private set; }

    public CancellationToken LocalMembershipLostToken => CancellationToken.None;

    public void SetSnapshot(params NodeLivenessSnapshot[] nodes)
    {
        lock (_lock)
        {
            _snapshot = nodes;
        }
    }

    public void SetEntries(params MembershipEventLogEntry[] entries)
    {
        lock (_lock)
        {
            _entries = entries;
        }
    }

    public ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
        CancellationToken cancellationToken = default
    )
    {
        lock (_lock)
        {
            SnapshotReads++;

            if (IsUnreachable)
            {
                throw new InvalidOperationException("The membership store did not answer.");
            }

            return ValueTask.FromResult(_snapshot);
        }
    }

    public IReadOnlyList<MembershipEventLogEntry> GetEntries()
    {
        lock (_lock)
        {
            return _entries;
        }
    }

    public ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask LeaveAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public async IAsyncEnumerable<NodeMembershipEvent> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await Task.CompletedTask;

        yield break;
    }

    public static NodeLivenessSnapshot Node(
        string identity,
        NodeLivenessState state = NodeLivenessState.Alive,
        string? role = "worker",
        DateTimeOffset? lastHeartbeatAt = null,
        params (string Key, string Value)[] metadata
    ) =>
        new(
            NodeIdentity.Parse(identity),
            state,
            role,
            metadata.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            lastHeartbeatAt ?? new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero)
        );

    /// <summary>The coordination settings a real provider would register, for a host the fake stands in for.</summary>
    public static CoordinationClusterSummary Summary(string clusterName = "orders") =>
        new()
        {
            ClusterName = clusterName,
            ProviderName = "PostgreSql",
            Role = "api",
            HeartbeatInterval = TimeSpan.FromSeconds(5),
            SuspicionThreshold = TimeSpan.FromSeconds(15),
            DeadThreshold = TimeSpan.FromSeconds(30),
            DeadRetentionWindow = TimeSpan.FromSeconds(30),
            SelfFenceMargin = TimeSpan.FromSeconds(5),
            MembershipHeartbeatEnabled = true,
        };
}
