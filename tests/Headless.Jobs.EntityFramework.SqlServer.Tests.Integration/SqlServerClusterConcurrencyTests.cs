// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the cluster-wide concurrency conformance suite against SqlServer.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerClusterConcurrencyTests(SqlServerJobsCoordinationFixture fixture)
    : JobsClusterConcurrencyConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task three_nodes_never_lease_a_limited_job_past_its_cluster_limit()
    {
        return base.three_nodes_never_lease_a_limited_job_past_its_cluster_limit();
    }

    [Fact]
    public override Task crashed_node_runs_stop_counting_once_their_leases_lapse()
    {
        return base.crashed_node_runs_stop_counting_once_their_leases_lapse();
    }

    [Fact]
    public override Task scheduler_peek_skips_a_limited_job_with_no_free_slot()
    {
        return base.scheduler_peek_skips_a_limited_job_with_no_free_slot();
    }

    [Fact]
    public override Task cron_occurrences_share_the_limit_with_time_jobs()
    {
        return base.cron_occurrences_share_the_limit_with_time_jobs();
    }

    [Fact]
    public override Task fallback_sweeps_on_two_nodes_lease_overdue_occurrences_up_to_the_limit()
    {
        return base.fallback_sweeps_on_two_nodes_lease_overdue_occurrences_up_to_the_limit();
    }

    [Fact]
    public override Task immediate_acquire_leaves_a_limited_job_to_the_scheduler_claim()
    {
        return base.immediate_acquire_leaves_a_limited_job_to_the_scheduler_claim();
    }

    [Fact]
    public override Task optimistic_claim_fallback_refuses_a_cluster_limit()
    {
        return base.optimistic_claim_fallback_refuses_a_cluster_limit();
    }
}
