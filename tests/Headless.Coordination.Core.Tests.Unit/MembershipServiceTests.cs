// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class MembershipServiceTests : TestBase
{
    [Fact]
    public async Task should_register_by_allocating_incarnation_and_writing_descriptor_without_heartbeating()
    {
        // given
        var store = new FakeMembershipStore { NextIncarnation = new NodeIncarnation(3) };
        var sut = _CreateService(store, nodeId: "node-a");

        // when
        var identity = await sut.RegisterAsync(AbortToken);

        // then
        identity.Should().Be(new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(3)));
        sut.Identity.Should().Be(identity);
        // UpsertDescriptorAsync now durably establishes both descriptor and liveness; register no longer beats.
        store.Descriptors.Should().ContainSingle().Which.Identity.Should().Be(identity);
        store.Heartbeats.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_only_alive_nodes_in_deterministic_order()
    {
        // given
        var store = new FakeMembershipStore();
        var nodeB = new NodeIdentity(new NodeId("node-b"), new NodeIncarnation(1));
        var nodeA = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(2));
        store.EnqueueSnapshot(
            new NodeLivenessSnapshot(
                nodeB,
                NodeLivenessState.Alive,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal)
            ),
            new NodeLivenessSnapshot(
                nodeA,
                NodeLivenessState.Alive,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal)
            ),
            new NodeLivenessSnapshot(
                new NodeIdentity(new NodeId("node-c"), new NodeIncarnation(1)),
                NodeLivenessState.Suspected,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal)
            )
        );
        var sut = _CreateService(store);

        // when
        var liveNodes = await sut.GetLiveNodesAsync(AbortToken);

        // then
        liveNodes.Should().Equal(nodeA, nodeB);
        store.ReadLiveNodesCalls.Should().Be(1);
        store.ReadLivenessCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_fire_local_loss_token_and_stop_application_when_self_heartbeat_is_rejected()
    {
        // given
        var store = new FakeMembershipStore();
        var lifetime = new FakeHostApplicationLifetime();
        var sut = _CreateService(store, lifetime: lifetime);
        var identity = await sut.RegisterAsync(AbortToken);
        store.HeartbeatAccepted = false;
        using var watcherCts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        watcherCts.CancelAfter(TimeSpan.FromSeconds(2));
        await using var watcher = sut.WatchAsync(watcherCts.Token).GetAsyncEnumerator(watcherCts.Token);

        // when
        var accepted = await sut.HeartbeatAsync(AbortToken);
        var hasEvent = await watcher.MoveNextAsync();

        // then
        accepted.Should().BeFalse();
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeTrue();
        sut.Identity.Should().BeNull();
        hasEvent.Should().BeTrue();
        watcher.Current.Should().BeOfType<LocalMembershipLost>().Which.Identity.Should().Be(identity);
        lifetime.StopApplicationCalled.Should().BeTrue();
    }

    [Fact]
    public async Task should_fire_local_loss_token_without_stopping_host_when_configured()
    {
        // given
        var options = new CoordinationOptions { MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly };
        var store = new FakeMembershipStore();
        var lifetime = new FakeHostApplicationLifetime();
        var sut = _CreateService(store, options, lifetime);
        await sut.RegisterAsync(AbortToken);
        store.HeartbeatAccepted = false;

        // when
        var accepted = await sut.HeartbeatAsync(AbortToken);

        // then
        accepted.Should().BeFalse();
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeTrue();
        lifetime.StopApplicationCalled.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_false_and_not_touch_store_when_heartbeat_called_before_register()
    {
        // given
        var store = new FakeMembershipStore();
        var sut = _CreateService(store);

        // when
        var accepted = await sut.HeartbeatAsync(AbortToken);

        // then
        accepted.Should().BeFalse();
        sut.Identity.Should().BeNull();
        store.Heartbeats.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_false_and_not_touch_store_when_heartbeat_called_after_leave()
    {
        // given
        var store = new FakeMembershipStore();
        var sut = _CreateService(store);
        await sut.RegisterAsync(AbortToken);
        await sut.LeaveAsync(AbortToken);
        store.Heartbeats.Clear();

        // when
        var accepted = await sut.HeartbeatAsync(AbortToken);

        // then
        accepted.Should().BeFalse();
        sut.Identity.Should().BeNull();
        store.Heartbeats.Should().BeEmpty();
    }

    [Theory]
    [InlineData(NodeLivenessState.Alive, true)]
    [InlineData(NodeLivenessState.Suspected, false)]
    [InlineData(NodeLivenessState.Dead, false)]
    public async Task should_map_targeted_node_liveness_state_to_is_alive(NodeLivenessState state, bool expected)
    {
        // given
        var store = new FakeMembershipStore();
        var identity = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1));
        store.NodeStates[identity] = state;
        var sut = _CreateService(store);

        // when
        var alive = await sut.IsAliveAsync(identity, AbortToken);

        // then
        alive.Should().Be(expected);
    }

    [Fact]
    public async Task should_treat_absent_targeted_node_as_not_alive()
    {
        // given
        var store = new FakeMembershipStore();
        var identity = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1));
        var sut = _CreateService(store);

        // when — identity was never configured in the store, so it resolves to absent (null)
        var alive = await sut.IsAliveAsync(identity, AbortToken);

        // then
        alive.Should().BeFalse();
    }

    [Fact]
    public async Task should_use_targeted_read_and_not_full_snapshot_for_is_alive()
    {
        // given
        var store = new FakeMembershipStore();
        var identity = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1));
        store.NodeStates[identity] = NodeLivenessState.Alive;
        var sut = _CreateService(store);

        // when
        await sut.IsAliveAsync(identity, AbortToken);

        // then — the targeted SPI method is used; the full cluster snapshot read is not
        store.ReadNodeLivenessCalls.Should().Be(1);
        store.ReadLivenessCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_throw_when_is_alive_called_with_cancelled_token()
    {
        // given
        var store = new FakeMembershipStore();
        var identity = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1));
        var sut = _CreateService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await sut.IsAliveAsync(identity, cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        store.ReadNodeLivenessCalls.Should().Be(0);
    }

    private static MembershipService _CreateService(
        FakeMembershipStore store,
        CoordinationOptions? options = null,
        IHostApplicationLifetime? lifetime = null,
        string nodeId = "node-a"
    )
    {
        return new MembershipService(
            store,
            new StaticNodeIdProvider(new NodeId(nodeId)),
            options ?? new CoordinationOptions(),
            new MembershipEventSource(NullLogger<MembershipEventSource>.Instance),
            lifetime,
            NullLogger<MembershipService>.Instance
        );
    }

    private sealed class StaticNodeIdProvider(NodeId nodeId) : INodeIdProvider
    {
        public ValueTask<NodeId> GetNodeIdAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(nodeId);
        }
    }
}
