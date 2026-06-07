// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Coordination;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.
public abstract class MembershipConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : ICoordinationFixture
{
    public virtual async Task should_register_and_appear_in_live_set()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);

        var identity = await node.Membership.RegisterAsync(AbortToken);
        var live = await node.Membership.GetLiveNodesAsync(AbortToken);
        var snapshot = await node.Membership.GetLivenessSnapshotAsync(AbortToken);

        identity.NodeId.Value.Should().Be("node-a");
        node.Membership.Identity.Should().Be(identity);
        live.Should().Equal([identity]);
        snapshot.Should().ContainSingle(x => x.Identity == identity && x.State == NodeLivenessState.Alive);
    }

    public virtual async Task should_keep_node_alive_after_heartbeat()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);

        await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(120), AbortToken);
        var accepted = await node.Membership.HeartbeatAsync(AbortToken);
        var live = await node.Membership.GetLiveNodesAsync(AbortToken);

        accepted.Should().BeTrue();
        live.Should().Equal([identity]);
    }

    public virtual async Task should_return_dead_snapshot_before_retention_prune()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);

        await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(450), AbortToken);

        var live = await node.Membership.GetLiveNodesAsync(AbortToken);
        var snapshot = await node.Membership.GetLivenessSnapshotAsync(AbortToken);

        live.Should().NotContain(identity);
        snapshot.Should().ContainSingle(x => x.Identity == identity && x.State == NodeLivenessState.Dead);
    }

    public virtual async Task should_remove_from_live_set_on_graceful_leave()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);

        await node.Membership.LeaveAsync(AbortToken);

        var live = await node.Membership.GetLiveNodesAsync(AbortToken);
        var snapshot = await node.Membership.GetLivenessSnapshotAsync(AbortToken);

        live.Should().NotContain(identity);
        snapshot.Should().ContainSingle(x => x.Identity == identity && x.State == NodeLivenessState.Dead);
    }

    public virtual async Task should_allocate_unique_increasing_incarnations_for_same_node_id()
    {
        var cluster = _Cluster();
        var nodes = new List<CoordinationNodeHandle>();

        try
        {
            for (var i = 0; i < 6; i++)
            {
                nodes.Add(await fixture.CreateNodeAsync(cluster, "node-a", AbortToken));
            }

            var identities = await Task.WhenAll(
                nodes.Select(node => node.Membership.RegisterAsync(AbortToken).AsTask())
            );

            identities.Select(x => x.NodeId.Value).Should().OnlyContain(x => x == "node-a");
            identities.Select(x => x.Incarnation.Value).Should().OnlyHaveUniqueItems();
            identities.Select(x => x.Incarnation.Value).Should().BeEquivalentTo([1L, 2L, 3L, 4L, 5L, 6L]);
        }
        finally
        {
            foreach (var node in nodes)
            {
                await node.DisposeAsync();
            }
        }
    }

    public virtual async Task should_filter_operational_reads_to_current_generation()
    {
        var cluster = _Cluster();
        await using var first = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);

        await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(450), AbortToken);

        await using var second = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var secondIdentity = await second.Membership.RegisterAsync(AbortToken);

        var live = await second.Membership.GetLiveNodesAsync(AbortToken);
        var snapshot = await second.Membership.GetLivenessSnapshotAsync(AbortToken);

        secondIdentity.Incarnation.Value.Should().BeGreaterThan(firstIdentity.Incarnation.Value);
        live.Should().Equal([secondIdentity]);
        snapshot.Should().ContainSingle(x => x.Identity == secondIdentity && x.State == NodeLivenessState.Alive);
        snapshot.Should().NotContain(x => x.Identity == firstIdentity);
    }

    public virtual async Task should_reject_stale_and_impossible_heartbeats_with_generation_guard()
    {
        var cluster = _Cluster();
        await using var first = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);

        await using var second = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var secondIdentity = await second.Membership.RegisterAsync(AbortToken);
        var store = second.Services.GetRequiredService<IMembershipStore>();
        var impossibleIdentity = new NodeIdentity(
            secondIdentity.NodeId,
            new NodeIncarnation(secondIdentity.Incarnation.Value + 1)
        );

        var staleAccepted = await store.HeartbeatAsync(firstIdentity, AbortToken);
        var impossibleAccepted = await store.HeartbeatAsync(impossibleIdentity, AbortToken);
        var currentAccepted = await store.HeartbeatAsync(secondIdentity, AbortToken);
        var live = await second.Membership.GetLiveNodesAsync(AbortToken);

        staleAccepted.Should().BeFalse();
        impossibleAccepted.Should().BeFalse();
        currentAccepted.Should().BeTrue();
        live.Should().Equal([secondIdentity]);
    }

    public virtual async Task should_reject_stale_heartbeat_after_retained_state_is_pruned()
    {
        var cluster = _Cluster();
        await using var first = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);

        await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(750), AbortToken);
        (await first.Membership.GetLivenessSnapshotAsync(AbortToken)).Should().BeEmpty();

        await using var second = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var secondIdentity = await second.Membership.RegisterAsync(AbortToken);
        var store = second.Services.GetRequiredService<IMembershipStore>();

        var staleAccepted = await store.HeartbeatAsync(firstIdentity, AbortToken);
        var live = await second.Membership.GetLiveNodesAsync(AbortToken);

        secondIdentity.Incarnation.Value.Should().BeGreaterThan(firstIdentity.Incarnation.Value);
        staleAccepted.Should().BeFalse();
        live.Should().Equal([secondIdentity]);
    }

    public virtual async Task should_isolate_generation_and_reads_by_cluster()
    {
        var firstCluster = _Cluster();
        var secondCluster = _Cluster();
        await using var first = await fixture.CreateNodeAsync(firstCluster, "node-a", AbortToken);
        await using var second = await fixture.CreateNodeAsync(secondCluster, "node-a", AbortToken);

        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);
        var secondIdentity = await second.Membership.RegisterAsync(AbortToken);
        var firstLive = await first.Membership.GetLiveNodesAsync(AbortToken);
        var secondLive = await second.Membership.GetLiveNodesAsync(AbortToken);

        firstIdentity.Should().Be(new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1)));
        secondIdentity.Should().Be(new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1)));
        firstLive.Should().Equal([firstIdentity]);
        secondLive.Should().Equal([secondIdentity]);
    }

    public virtual async Task should_publish_local_lost_event_when_incarnation_is_superseded()
    {
        var cluster = _Cluster();
        await using var first = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);
        using var watcherCts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        watcherCts.CancelAfter(TimeSpan.FromSeconds(2));
        await using var watcher = first.Membership.WatchAsync(watcherCts.Token).GetAsyncEnumerator(watcherCts.Token);

        await using var second = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        _ = await second.Membership.RegisterAsync(AbortToken);

        var accepted = await first.Membership.HeartbeatAsync(AbortToken);
        var hasEvent = await watcher.MoveNextAsync();

        accepted.Should().BeFalse();
        hasEvent.Should().BeTrue();
        watcher.Current.Should().BeOfType<LocalMembershipLost>().Which.Identity.Should().Be(firstIdentity);
    }

    public virtual async Task should_return_ordered_live_nodes()
    {
        var cluster = _Cluster();
        await using var nodeB = await fixture.CreateNodeAsync(cluster, "node-b", AbortToken);
        await using var nodeA = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);

        var identityB = await nodeB.Membership.RegisterAsync(AbortToken);
        var identityA = await nodeA.Membership.RegisterAsync(AbortToken);

        var live = await nodeB.Membership.GetLiveNodesAsync(AbortToken);

        live.Should().Equal([identityA, identityB]);
    }

    public virtual async Task should_report_failover_eligible_provider()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);

        var capabilities = node.Services.GetRequiredService<ProviderCapabilities>();

        capabilities.FailoverEligible.Should().BeTrue();
    }

    public virtual async Task should_fail_stop_when_local_incarnation_is_superseded()
    {
        var cluster = _Cluster();
        await using var first = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);

        await using var second = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var secondIdentity = await second.Membership.RegisterAsync(AbortToken);

        var accepted = await first.Membership.HeartbeatAsync(AbortToken);
        var live = await second.Membership.GetLiveNodesAsync(AbortToken);

        secondIdentity.Incarnation.Value.Should().BeGreaterThan(firstIdentity.Incarnation.Value);
        accepted.Should().BeFalse();
        first.Membership.LocalMembershipLostToken.IsCancellationRequested.Should().BeTrue();
        live.Should().Equal([secondIdentity]);
    }

    private static string _Cluster()
    {
        return "conformance-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
    }
}
#pragma warning restore CA1707
