// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class MembershipServiceTests : TestBase
{
    [Fact]
    public async Task should_register_by_allocating_incarnation_writing_descriptor_and_heartbeating()
    {
        // given
        var store = new FakeMembershipStore { NextIncarnation = new NodeIncarnation(3) };
        var sut = _CreateService(store, nodeId: "node-a");

        // when
        var identity = await sut.RegisterAsync(AbortToken);

        // then
        identity.Should().Be(new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(3)));
        sut.Identity.Should().Be(identity);
        store.Descriptors.Should().ContainSingle().Which.Identity.Should().Be(identity);
        store.Heartbeats.Should().ContainSingle().Which.Should().Be(identity);
    }

    [Fact]
    public async Task should_return_only_alive_nodes_in_deterministic_order()
    {
        // given
        var store = new FakeMembershipStore();
        var nodeB = new NodeIdentity(new NodeId("node-b"), new NodeIncarnation(1));
        var nodeA = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(2));
        store.EnqueueSnapshot(
            new NodeLivenessSnapshot(nodeB, NodeLivenessState.Alive, null, new Dictionary<string, string>()),
            new NodeLivenessSnapshot(nodeA, NodeLivenessState.Alive, null, new Dictionary<string, string>()),
            new NodeLivenessSnapshot(
                new NodeIdentity(new NodeId("node-c"), new NodeIncarnation(1)),
                NodeLivenessState.Suspected,
                null,
                new Dictionary<string, string>()
            )
        );
        var sut = _CreateService(store);

        // when
        var liveNodes = await sut.GetLiveNodesAsync(AbortToken);

        // then
        liveNodes.Should().Equal(nodeA, nodeB);
    }

    [Fact]
    public async Task should_fire_local_loss_token_and_stop_application_when_self_heartbeat_is_rejected()
    {
        // given
        var store = new FakeMembershipStore { HeartbeatAccepted = false };
        var lifetime = new FakeHostApplicationLifetime();
        var sut = _CreateService(store, lifetime: lifetime);
        await sut.RegisterAsync(AbortToken);

        // when
        var accepted = await sut.HeartbeatAsync(AbortToken);

        // then
        accepted.Should().BeFalse();
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeTrue();
        lifetime.StopApplicationCalled.Should().BeTrue();
    }

    [Fact]
    public async Task should_fire_local_loss_token_without_stopping_host_when_configured()
    {
        // given
        var options = new CoordinationOptions { MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly };
        var store = new FakeMembershipStore { HeartbeatAccepted = false };
        var lifetime = new FakeHostApplicationLifetime();
        var sut = _CreateService(store, options, lifetime);
        await sut.RegisterAsync(AbortToken);

        // when
        var accepted = await sut.HeartbeatAsync(AbortToken);

        // then
        accepted.Should().BeFalse();
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeTrue();
        lifetime.StopApplicationCalled.Should().BeFalse();
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
