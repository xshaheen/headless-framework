// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Coordination;
using Headless.Jobs.Coordination;
using Headless.Jobs.Entities;
using Headless.Jobs.Infrastructure.Dashboard;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Dashboard;

public sealed class MembershipDashboardBridgeTests
{
    private static NodeIdentity _Identity(string node, long incarnation) =>
        new(new NodeId(node), new NodeIncarnation(incarnation));

    private static NodeLivenessSnapshot _Snapshot(
        string node,
        long incarnation,
        NodeLivenessState state,
        string? role
    ) => new(_Identity(node, incarnation), state, role, new Dictionary<string, string>(StringComparer.Ordinal));

    private static (MembershipDashboardBridge Bridge, IJobsNotificationHubSender Sender) _Create()
    {
        var membership = new FakeMembership();
        var sender = Substitute.For<IJobsNotificationHubSender>();
        var bridge = new MembershipDashboardBridge(membership, sender, NullLogger<MembershipDashboardBridge>.Instance);

        return (bridge, sender);
    }

    // Reads a property by name off the anonymous push payload the bridge sends to the hub.
    private static string? _ReadString(object payload, string propertyName)
    {
        var property = payload.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

        return property?.GetValue(payload) as string;
    }

    [Fact]
    public async Task should_push_node_state_on_node_joined()
    {
        var (bridge, sender) = _Create();

        await bridge.HandleEventAsync(new NodeJoined(_Identity("node-a", 5)));

        await sender
            .Received(1)
            .UpdateNodesAsync(
                Arg.Is<object>(p => _ReadString(p, "identity") == "node-a@5" && _ReadString(p, "state") == "Alive")
            );
    }

    [Fact]
    public async Task should_push_node_state_on_node_left_as_dead()
    {
        var (bridge, sender) = _Create();

        await bridge.HandleEventAsync(new NodeLeft(_Identity("node-b", 9)));

        await sender
            .Received(1)
            .UpdateNodesAsync(
                Arg.Is<object>(p => _ReadString(p, "identity") == "node-b@9" && _ReadString(p, "state") == "Dead")
            );
    }

    [Fact]
    public async Task should_push_suspected_node_with_suspected_state_not_dropped()
    {
        var (bridge, sender) = _Create();

        await bridge.HandleEventAsync(new NodeSuspected(_Identity("node-c", 3)));

        await sender
            .Received(1)
            .UpdateNodesAsync(
                Arg.Is<object>(p => _ReadString(p, "identity") == "node-c@3" && _ReadString(p, "state") == "Suspected")
            );
    }

    [Fact]
    public async Task should_not_push_on_local_membership_lost()
    {
        var (bridge, sender) = _Create();

        await bridge.HandleEventAsync(new LocalMembershipLost(_Identity("node-a", 5)));

        await sender.DidNotReceive().UpdateNodesAsync(Arg.Any<object>());
    }

    [Fact]
    public void should_project_two_alive_nodes_with_role_and_state()
    {
        IReadOnlyList<NodeLivenessSnapshot> snapshot =
        [
            _Snapshot("node-a", 1, NodeLivenessState.Alive, "worker"),
            _Snapshot("node-b", 2, NodeLivenessState.Alive, "leader"),
        ];

        var views = JobsDashboardRepository<TimeJobEntity, CronJobEntity>.ProjectLiveNodes(snapshot);

        views.Should().HaveCount(2);
        views.Should().ContainSingle(v => v.Identity == "node-a@1" && v.State == "Alive" && v.Role == "worker");
        views.Should().ContainSingle(v => v.Identity == "node-b@2" && v.State == "Alive" && v.Role == "leader");
    }

    [Fact]
    public void should_project_empty_snapshot_to_empty_list()
    {
        IReadOnlyList<NodeLivenessSnapshot> snapshot = [];

        var views = JobsDashboardRepository<TimeJobEntity, CronJobEntity>.ProjectLiveNodes(snapshot);

        views.Should().BeEmpty();
    }

    [Fact]
    public void should_surface_last_beat_from_node_metadata()
    {
        IReadOnlyList<NodeLivenessSnapshot> snapshot =
        [
            new NodeLivenessSnapshot(
                _Identity("node-a", 1),
                NodeLivenessState.Alive,
                Role: null,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["last_beat"] = "2026-06-07T10:00:00Z",
                }
            ),
        ];

        var views = JobsDashboardRepository<TimeJobEntity, CronJobEntity>.ProjectLiveNodes(snapshot);

        views.Should().ContainSingle().Which.LastBeat.Should().Be("2026-06-07T10:00:00Z");
    }

    private sealed class FakeMembership : INodeMembership
    {
        public NodeIdentity? Identity => null;

        public CancellationToken LocalMembershipLostToken => CancellationToken.None;

        public ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(default(NodeIdentity));

        public ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);

        public ValueTask LeaveAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult<IReadOnlyList<NodeIdentity>>([]);

        public ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult<IReadOnlyList<NodeLivenessSnapshot>>([]);

        public async IAsyncEnumerable<NodeMembershipEvent> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.CompletedTask.ConfigureAwait(false);

            yield break;
        }
    }
}
