// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the typed-chain runtime conformance suite against Postgres.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlChainConformanceTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsChainConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task enqueue_persists_conditional_tree_edges()
    {
        return base.enqueue_persists_conditional_tree_edges();
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
}
