// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the typed-chain runtime conformance suite against SQL Server.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerChainConformanceTests(SqlServerJobsCoordinationFixture fixture)
    : JobsChainConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task enqueue_persists_conditional_tree_edges()
    {
        return base.enqueue_persists_conditional_tree_edges();
    }

    [Fact]
    public override Task deleting_a_chain_root_removes_every_descendant_row()
    {
        return base.deleting_a_chain_root_removes_every_descendant_row();
    }

    [Fact]
    public override Task deep_chain_claim_stamps_every_descendant_to_configured_depth()
    {
        return base.deep_chain_claim_stamps_every_descendant_to_configured_depth();
    }

    [Fact]
    public override Task chain_enqueue_rolls_back_atomically_leaving_no_rows()
    {
        return base.chain_enqueue_rolls_back_atomically_leaving_no_rows();
    }

    [Fact]
    public override Task timed_child_is_not_claimable_while_parent_is_non_terminal()
    {
        return base.timed_child_is_not_claimable_while_parent_is_non_terminal();
    }

    [Fact]
    public override Task parent_success_releases_timed_success_child_and_skips_timed_catch_child()
    {
        return base.parent_success_releases_timed_success_child_and_skips_timed_catch_child();
    }

    [Fact]
    public override Task future_timed_success_child_waits_for_its_own_time_then_becomes_claimable()
    {
        return base.future_timed_success_child_waits_for_its_own_time_then_becomes_claimable();
    }

    [Fact]
    public override Task parent_failure_skips_timed_success_subtree_and_releases_timed_catch_child()
    {
        return base.parent_failure_skips_timed_success_subtree_and_releases_timed_catch_child();
    }

    [Fact]
    public override Task dead_node_reclaim_resumes_chain_without_skipping_children()
    {
        return base.dead_node_reclaim_resumes_chain_without_skipping_children();
    }

    [Fact]
    public override Task immediate_acquire_leases_the_whole_non_timed_subtree()
    {
        return base.immediate_acquire_leases_the_whole_non_timed_subtree();
    }

    [Fact]
    public override Task bounded_sweep_drains_a_large_mismatched_backlog_without_starving_on_matching_children()
    {
        return base.bounded_sweep_drains_a_large_mismatched_backlog_without_starving_on_matching_children();
    }

    [Fact]
    public override Task bounded_sweep_skips_a_mismatched_child_whole_subtree_in_one_pass()
    {
        return base.bounded_sweep_skips_a_mismatched_child_whole_subtree_in_one_pass();
    }

    [Fact]
    public override Task cas_frontier_fence_rejects_descendants_when_root_lease_expires_mid_walk()
    {
        return base.cas_frontier_fence_rejects_descendants_when_root_lease_expires_mid_walk();
    }

    [Fact]
    public override Task cas_frontier_fence_rejects_descendants_when_root_is_stolen_mid_walk()
    {
        return base.cas_frontier_fence_rejects_descendants_when_root_is_stolen_mid_walk();
    }

    [Fact]
    public override Task two_owner_root_race_leaves_no_split_ownership()
    {
        return base.two_owner_root_race_leaves_no_split_ownership();
    }

    [Fact]
    public override Task native_claim_contention_gives_one_owner_the_whole_subtree()
    {
        return base.native_claim_contention_gives_one_owner_the_whole_subtree();
    }

    [Fact]
    public override Task native_sql_gate_matches_the_shared_rules_across_the_grid()
    {
        return base.native_sql_gate_matches_the_shared_rules_across_the_grid();
    }

    [Fact]
    public override Task timed_child_of_a_skipped_parent_is_swept_to_skipped()
    {
        return base.timed_child_of_a_skipped_parent_is_swept_to_skipped();
    }

    [Fact]
    public override Task deep_chain_claim_truncates_at_configured_depth_without_erroring()
    {
        return base.deep_chain_claim_truncates_at_configured_depth_without_erroring();
    }
}
