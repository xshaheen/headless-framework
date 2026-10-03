// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the failed-job requeue conformance suite against SQL Server.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerRequeueTests(SqlServerJobsCoordinationFixture fixture)
    : JobsRequeueConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task failed_time_job_returns_to_idle_due_now_and_is_claimed_by_the_main_peek()
    {
        return base.failed_time_job_returns_to_idle_due_now_and_is_claimed_by_the_main_peek();
    }

    [Fact]
    public override Task time_job_requeue_stamps_the_database_clock_not_the_node_clock()
    {
        return base.time_job_requeue_stamps_the_database_clock_not_the_node_clock();
    }

    [Fact]
    public override Task time_job_that_is_not_failed_is_refused_and_unchanged()
    {
        return base.time_job_that_is_not_failed_is_refused_and_unchanged();
    }

    [Fact]
    public override Task chain_parent_and_chain_child_are_refused()
    {
        return base.chain_parent_and_chain_child_are_refused();
    }

    [Fact]
    public override Task current_keyed_generation_is_requeued_and_a_superseded_one_is_refused()
    {
        return base.current_keyed_generation_is_requeued_and_a_superseded_one_is_refused();
    }

    [Fact]
    public override Task concurrent_time_job_requeues_move_the_row_once()
    {
        return base.concurrent_time_job_requeues_move_the_row_once();
    }

    [Fact]
    public override Task failed_occurrence_returns_to_idle_keeps_its_instant_and_is_claimed_by_the_fallback()
    {
        return base.failed_occurrence_returns_to_idle_keeps_its_instant_and_is_claimed_by_the_fallback();
    }

    [Fact]
    public override Task occurrence_that_is_not_failed_is_refused()
    {
        return base.occurrence_that_is_not_failed_is_refused();
    }

    [Fact]
    public override Task overlap_policy_decides_a_requeue_next_to_a_running_occurrence()
    {
        return base.overlap_policy_decides_a_requeue_next_to_a_running_occurrence();
    }

    [Fact]
    public override Task skip_overlap_requeues_when_no_other_occurrence_is_unfinished()
    {
        return base.skip_overlap_requeues_when_no_other_occurrence_is_unfinished();
    }

    [Fact]
    public override Task occurrence_whose_instant_is_held_by_a_live_row_is_refused()
    {
        return base.occurrence_whose_instant_is_held_by_a_live_row_is_refused();
    }

    [Fact]
    public override Task concurrent_occurrence_requeues_move_the_row_once()
    {
        return base.concurrent_occurrence_requeues_move_the_row_once();
    }
}
