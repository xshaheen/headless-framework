// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection(nameof(RedisMembershipFixture))]
public sealed class RedisMembershipConformanceTests(RedisMembershipFixture fixture)
    : MembershipConformanceTests<RedisMembershipFixture>(fixture)
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
    public override Task should_return_ordered_live_nodes() => base.should_return_ordered_live_nodes();
}
