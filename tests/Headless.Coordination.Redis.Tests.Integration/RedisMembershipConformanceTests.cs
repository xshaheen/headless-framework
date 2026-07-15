// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Coordination.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Tests;

[Collection(nameof(RedisMembershipFixture))]
public sealed class RedisMembershipConformanceTests(RedisMembershipFixture fixture)
    : MembershipConformanceTests<RedisMembershipFixture>(fixture)
{
    private readonly RedisMembershipFixture _fixture = fixture;

    [Fact]
    public override Task should_register_and_appear_in_live_set()
    {
        return base.should_register_and_appear_in_live_set();
    }

    [Fact]
    public override Task should_keep_node_alive_after_heartbeat()
    {
        return base.should_keep_node_alive_after_heartbeat();
    }

    [Fact]
    public override Task should_return_dead_snapshot_before_retention_prune()
    {
        return base.should_return_dead_snapshot_before_retention_prune();
    }

    [Fact]
    public override Task should_remove_from_live_set_on_graceful_leave()
    {
        return base.should_remove_from_live_set_on_graceful_leave();
    }

    [Fact]
    public override Task should_allocate_unique_increasing_incarnations_for_same_node_id()
    {
        return base.should_allocate_unique_increasing_incarnations_for_same_node_id();
    }

    [Fact]
    public override Task should_filter_operational_reads_to_current_generation()
    {
        return base.should_filter_operational_reads_to_current_generation();
    }

    [Fact]
    public override Task should_reject_stale_and_impossible_heartbeats_with_generation_guard()
    {
        return base.should_reject_stale_and_impossible_heartbeats_with_generation_guard();
    }

    [Fact]
    public override Task should_reject_heartbeat_for_dead_current_incarnation()
    {
        return base.should_reject_heartbeat_for_dead_current_incarnation();
    }

    [Fact]
    public override Task should_reject_heartbeat_after_graceful_leave()
    {
        return base.should_reject_heartbeat_after_graceful_leave();
    }

    [Fact]
    public override Task should_reject_heartbeat_after_current_incarnation_is_pruned()
    {
        return base.should_reject_heartbeat_after_current_incarnation_is_pruned();
    }

    [Fact]
    public override Task should_reject_stale_heartbeat_after_retained_state_is_pruned()
    {
        return base.should_reject_stale_heartbeat_after_retained_state_is_pruned();
    }

    [Fact]
    public override Task should_isolate_generation_and_reads_by_cluster()
    {
        return base.should_isolate_generation_and_reads_by_cluster();
    }

    [Fact]
    public override Task should_publish_local_lost_event_when_incarnation_is_superseded()
    {
        return base.should_publish_local_lost_event_when_incarnation_is_superseded();
    }

    [Fact]
    public override Task should_return_ordered_live_nodes()
    {
        return base.should_return_ordered_live_nodes();
    }

    [Fact]
    public override Task should_fail_stop_when_local_incarnation_is_superseded()
    {
        return base.should_fail_stop_when_local_incarnation_is_superseded();
    }

    [Fact]
    public override Task should_stop_application_when_self_heartbeat_is_rejected()
    {
        return base.should_stop_application_when_self_heartbeat_is_rejected();
    }

    [Fact]
    public override Task should_not_evict_current_incarnation_when_prior_incarnation_leaves()
    {
        return base.should_not_evict_current_incarnation_when_prior_incarnation_leaves();
    }

    [Fact]
    public override Task should_read_targeted_node_liveness_across_states()
    {
        return base.should_read_targeted_node_liveness_across_states();
    }

    [Fact]
    public override Task should_read_dead_targeted_state_after_graceful_leave()
    {
        return base.should_read_dead_targeted_state_after_graceful_leave();
    }

    [Fact]
    public override Task should_read_null_targeted_state_for_superseded_incarnation()
    {
        return base.should_read_null_targeted_state_for_superseded_incarnation();
    }

    [Fact]
    public override Task should_read_null_without_pruning_for_retention_expired_node()
    {
        return base.should_read_null_without_pruning_for_retention_expired_node();
    }

    [Fact]
    public override Task should_agree_between_targeted_read_and_snapshot()
    {
        return base.should_agree_between_targeted_read_and_snapshot();
    }

    [Fact]
    public override Task should_derive_is_alive_from_targeted_read()
    {
        return base.should_derive_is_alive_from_targeted_read();
    }

    /// <summary>
    /// Documents the intentional cross-provider divergence (plan KTD-16): at the provider-default
    /// <see cref="RedisCoordinationOptions.RedisKnownNodeRetention"/> (7 days), Redis keeps Dead/Left nodes
    /// in the snapshot for the retention window — consumers filter by <see cref="NodeLivenessState"/> — whereas
    /// the relational providers prune shortly after the dead threshold.
    /// </summary>
    [Fact]
    public async Task should_retain_dead_node_in_snapshot_at_default_known_node_retention()
    {
        var cluster = "conformance-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionMultiplexer>(_fixture.ConnectionMultiplexer);
        services.AddHeadlessCoordination(setup =>
        {
            // Provider-default RedisKnownNodeRetention (7 days), not the fixture's 600ms override.
            setup.UseRedis(static _ => { });
            setup.Configure(options =>
            {
                options.ClusterName = cluster;
                options.ConfiguredNodeId = "node-a";
                // Mirror the harness thresholds (scaled by TimeScale) so this Redis-only test shares the same
                // explicit wall-clock margins as the shared conformance suite.
                options.HeartbeatInterval = CoordinationFixtureExtensions.HeartbeatInterval;
                options.SuspicionThreshold = CoordinationFixtureExtensions.SuspicionThreshold;
                options.DeadThreshold = CoordinationFixtureExtensions.DeadThreshold;
                options.DeadRetentionWindow = CoordinationFixtureExtensions.DeadRetentionWindow;
                options.MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly;
            });
        });

        await using var provider = services.BuildServiceProvider();

        foreach (var initializer in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(AbortToken);
        }

        var membership = provider.GetRequiredService<INodeMembership>();
        var identity = await membership.RegisterAsync(AbortToken);

        await TimeProvider.System.Delay(CoordinationFixtureExtensions.DeadButRetainedWait, AbortToken);

        var snapshot = await membership.GetLivenessSnapshotAsync(AbortToken);

        snapshot.Should().ContainSingle(x => x.Identity == identity && x.State == NodeLivenessState.Dead);
    }
}
