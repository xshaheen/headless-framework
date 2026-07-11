// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;
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

        // Stay below the suspicion threshold so the node is unambiguously still alive when we beat.
        await TimeProvider.System.Delay(CoordinationFixtureExtensions.SuspicionThreshold * 0.8, AbortToken);
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

        await TimeProvider.System.Delay(CoordinationFixtureExtensions.DeadButRetainedWait, AbortToken);

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
            identities.Should().OnlyHaveUniqueItems(x => x.Incarnation.Value);
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

        await TimeProvider.System.Delay(CoordinationFixtureExtensions.DeadButRetainedWait, AbortToken);

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

    public virtual async Task should_reject_heartbeat_for_dead_current_incarnation()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var store = node.Services.GetRequiredService<IMembershipStore>();

        await TimeProvider.System.Delay(CoordinationFixtureExtensions.DeadButRetainedWait, AbortToken);

        (await store.HeartbeatAsync(identity, AbortToken)).Should().BeFalse();
    }

    public virtual async Task should_reject_heartbeat_after_graceful_leave()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var store = node.Services.GetRequiredService<IMembershipStore>();
        await store.LeaveAsync(identity, AbortToken);

        (await store.HeartbeatAsync(identity, AbortToken)).Should().BeFalse();
    }

    public virtual async Task should_reject_heartbeat_after_current_incarnation_is_pruned()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var store = node.Services.GetRequiredService<IMembershipStore>();

        await TimeProvider.System.Delay(CoordinationFixtureExtensions.AfterPruneWait, AbortToken);
        (await node.Membership.GetLivenessSnapshotAsync(AbortToken)).Should().BeEmpty();

        (await store.HeartbeatAsync(identity, AbortToken)).Should().BeFalse();
    }

    public virtual async Task should_reject_stale_heartbeat_after_retained_state_is_pruned()
    {
        var cluster = _Cluster();
        await using var first = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);

        await TimeProvider.System.Delay(CoordinationFixtureExtensions.AfterPruneWait, AbortToken);
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

    public virtual async Task should_stop_application_when_self_heartbeat_is_rejected()
    {
        var cluster = _Cluster();
        await using var first = await fixture.CreateNodeAsync(
            cluster,
            "node-a",
            MembershipLostBehavior.StopApplication,
            AbortToken
        );
        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);

        await using var second = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var secondIdentity = await second.Membership.RegisterAsync(AbortToken);

        var accepted = await first.Membership.HeartbeatAsync(AbortToken);

        secondIdentity.Incarnation.Value.Should().BeGreaterThan(firstIdentity.Incarnation.Value);
        accepted.Should().BeFalse();
        first.Lifetime.StopApplicationCalled.Should().BeTrue();
    }

    public virtual async Task should_not_evict_current_incarnation_when_prior_incarnation_leaves()
    {
        var cluster = _Cluster();
        await using var first = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);

        await using var second = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var secondIdentity = await second.Membership.RegisterAsync(AbortToken);

        // Leaving the superseded (prior) incarnation must not remove the current generation from the live set.
        var firstStore = first.Services.GetRequiredService<IMembershipStore>();
        await firstStore.LeaveAsync(firstIdentity, AbortToken);

        var live = await second.Membership.GetLiveNodesAsync(AbortToken);
        var snapshot = await second.Membership.GetLivenessSnapshotAsync(AbortToken);

        secondIdentity.Incarnation.Value.Should().BeGreaterThan(firstIdentity.Incarnation.Value);
        live.Should().Equal([secondIdentity]);
        snapshot.Should().ContainSingle(x => x.Identity == secondIdentity && x.State == NodeLivenessState.Alive);
    }

    public virtual async Task should_read_targeted_node_liveness_across_states()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var store = node.Services.GetRequiredService<IMembershipStore>();

        // Alive immediately after register.
        (await store.ReadNodeLivenessAsync(identity, AbortToken))
            .Should()
            .Be(NodeLivenessState.Alive);

        // Aged into the suspicion band -> Suspected.
        await TimeProvider.System.Delay(CoordinationFixtureExtensions.SuspectedWait, AbortToken);
        (await store.ReadNodeLivenessAsync(identity, AbortToken)).Should().Be(NodeLivenessState.Suspected);

        // Aged past the dead threshold but still inside the retention window -> Dead.
        await TimeProvider.System.Delay(
            CoordinationFixtureExtensions.DeadButRetainedWait - CoordinationFixtureExtensions.SuspectedWait,
            AbortToken
        );
        (await store.ReadNodeLivenessAsync(identity, AbortToken)).Should().Be(NodeLivenessState.Dead);
    }

    public virtual async Task should_read_dead_targeted_state_after_graceful_leave()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var store = node.Services.GetRequiredService<IMembershipStore>();

        await node.Membership.LeaveAsync(AbortToken);

        (await store.ReadNodeLivenessAsync(identity, AbortToken)).Should().Be(NodeLivenessState.Dead);
    }

    public virtual async Task should_read_null_targeted_state_for_superseded_incarnation()
    {
        var cluster = _Cluster();
        await using var first = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);

        await using var second = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var secondIdentity = await second.Membership.RegisterAsync(AbortToken);
        var store = second.Services.GetRequiredService<IMembershipStore>();

        secondIdentity.Incarnation.Value.Should().BeGreaterThan(firstIdentity.Incarnation.Value);
        // The superseded prior incarnation is not current-generation -> absent (null).
        (await store.ReadNodeLivenessAsync(firstIdentity, AbortToken))
            .Should()
            .BeNull();
        (await store.ReadNodeLivenessAsync(secondIdentity, AbortToken)).Should().Be(NodeLivenessState.Alive);
    }

    public virtual async Task should_read_null_without_pruning_for_retention_expired_node()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var store = node.Services.GetRequiredService<IMembershipStore>();

        // Age past the retention window, then read the TARGETED path first — before any snapshot read, whose
        // prune side effect would delete the row and mask a divergence. The targeted path must reproduce the
        // snapshot's post-prune "absent" view by returning null, never by classifying the row as Dead.
        await TimeProvider.System.Delay(CoordinationFixtureExtensions.AfterPruneWait, AbortToken);

        var targeted = await store.ReadNodeLivenessAsync(identity, AbortToken);

        targeted.Should().BeNull();
        // The snapshot (read after) agrees the node is absent — and only now is the row eligible to be pruned.
        var snapshot = await node.Membership.GetLivenessSnapshotAsync(AbortToken);
        snapshot.Should().NotContain(x => x.Identity == identity);
    }

    public virtual async Task should_agree_between_targeted_read_and_snapshot()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var store = node.Services.GetRequiredService<IMembershipStore>();
        var unregistered = new NodeIdentity(new NodeId("node-z"), new NodeIncarnation(1));

        // Within the retention window the targeted read must equal the snapshot's per-identity state across every
        // classification — Alive, Suspected, and Dead-but-retained — and null exactly when the identity is absent
        // from the snapshot. Each band stays inside the retention window, so the snapshot read's prune never
        // deletes the row and masks a divergence; advancing the store clock walks the same row through all three
        // states. Pinning each band to its expected literal stops the agreement assertion from passing vacuously
        // when both paths classify the same wrong state.

        // Alive immediately after register, plus the absent identity.
        var aliveSnapshot = await node.Membership.GetLivenessSnapshotAsync(AbortToken);
        var alive = await store.ReadNodeLivenessAsync(identity, AbortToken);
        var absent = await store.ReadNodeLivenessAsync(unregistered, AbortToken);

        alive.Should().Be(aliveSnapshot.FirstOrDefault(x => x.Identity == identity)?.State);
        alive.Should().Be(NodeLivenessState.Alive);
        absent.Should().Be(aliveSnapshot.FirstOrDefault(x => x.Identity == unregistered)?.State);
        absent.Should().BeNull();

        // Suspected.
        await TimeProvider.System.Delay(CoordinationFixtureExtensions.SuspectedWait, AbortToken);
        var suspectedSnapshot = await node.Membership.GetLivenessSnapshotAsync(AbortToken);
        var suspected = await store.ReadNodeLivenessAsync(identity, AbortToken);
        suspected.Should().Be(suspectedSnapshot.FirstOrDefault(x => x.Identity == identity)?.State);
        suspected.Should().Be(NodeLivenessState.Suspected);

        // Dead, still inside the retention window.
        await TimeProvider.System.Delay(
            CoordinationFixtureExtensions.DeadButRetainedWait - CoordinationFixtureExtensions.SuspectedWait,
            AbortToken
        );
        var deadSnapshot = await node.Membership.GetLivenessSnapshotAsync(AbortToken);
        var dead = await store.ReadNodeLivenessAsync(identity, AbortToken);
        dead.Should().Be(deadSnapshot.FirstOrDefault(x => x.Identity == identity)?.State);
        dead.Should().Be(NodeLivenessState.Dead);
    }

    public virtual async Task should_derive_is_alive_from_targeted_read()
    {
        var cluster = _Cluster();
        await using var first = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);

        await using var second = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var secondIdentity = await second.Membership.RegisterAsync(AbortToken);

        // The current incarnation is alive; the superseded prior incarnation is not.
        (await second.Membership.IsAliveAsync(secondIdentity, AbortToken))
            .Should()
            .BeTrue();
        (await second.Membership.IsAliveAsync(firstIdentity, AbortToken)).Should().BeFalse();

        // IsAliveAsync maps every non-Alive targeted state to false, not just absence. As the current-generation
        // node ages out of the alive band its targeted state becomes Suspected, then Dead — both derive false
        // (only Alive is true), so this pins the R4 derivation beyond the superseded/absent case above.
        await TimeProvider.System.Delay(CoordinationFixtureExtensions.SuspectedWait, AbortToken);
        (await second.Membership.IsAliveAsync(secondIdentity, AbortToken)).Should().BeFalse();

        await TimeProvider.System.Delay(
            CoordinationFixtureExtensions.DeadButRetainedWait - CoordinationFixtureExtensions.SuspectedWait,
            AbortToken
        );
        (await second.Membership.IsAliveAsync(secondIdentity, AbortToken)).Should().BeFalse();
    }

    private static string _Cluster()
    {
        return "conformance-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
    }
}
#pragma warning restore CA1707
