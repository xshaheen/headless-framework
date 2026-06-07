// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgresMembershipFixture>]
public sealed class PostgresMembershipConformanceTests(PostgresMembershipFixture fixture)
    : MembershipConformanceTests<PostgresMembershipFixture>(fixture)
{
    [Fact]
    public override Task should_register_and_appear_in_live_set() => base.should_register_and_appear_in_live_set();

    [Fact]
    public override Task should_keep_node_alive_after_heartbeat() => base.should_keep_node_alive_after_heartbeat();

    [Fact]
    public override Task should_return_dead_snapshot_before_retention_prune() =>
        base.should_return_dead_snapshot_before_retention_prune();

    [Fact]
    public override Task should_remove_from_live_set_on_graceful_leave() =>
        base.should_remove_from_live_set_on_graceful_leave();

    [Fact]
    public override Task should_allocate_unique_increasing_incarnations_for_same_node_id() =>
        base.should_allocate_unique_increasing_incarnations_for_same_node_id();

    [Fact]
    public override Task should_filter_operational_reads_to_current_generation() =>
        base.should_filter_operational_reads_to_current_generation();

    [Fact]
    public override Task should_reject_stale_and_impossible_heartbeats_with_generation_guard() =>
        base.should_reject_stale_and_impossible_heartbeats_with_generation_guard();

    [Fact]
    public override Task should_reject_stale_heartbeat_after_retained_state_is_pruned() =>
        base.should_reject_stale_heartbeat_after_retained_state_is_pruned();

    [Fact]
    public override Task should_isolate_generation_and_reads_by_cluster() =>
        base.should_isolate_generation_and_reads_by_cluster();

    [Fact]
    public override Task should_publish_local_lost_event_when_incarnation_is_superseded() =>
        base.should_publish_local_lost_event_when_incarnation_is_superseded();

    [Fact]
    public override Task should_return_ordered_live_nodes() => base.should_return_ordered_live_nodes();

    [Fact]
    public override Task should_report_failover_eligible_provider() => base.should_report_failover_eligible_provider();

    [Fact]
    public override Task should_fail_stop_when_local_incarnation_is_superseded() =>
        base.should_fail_stop_when_local_incarnation_is_superseded();
}
