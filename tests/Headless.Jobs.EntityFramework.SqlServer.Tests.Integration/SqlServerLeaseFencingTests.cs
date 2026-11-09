// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the lease fencing conformance suite against SqlServer.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerLeaseFencingTests(SqlServerJobsCoordinationFixture fixture)
    : JobsLeaseFencingConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task same_owner_reclaim_of_an_expired_row_refuses_the_earlier_attempts_completion()
    {
        return base.same_owner_reclaim_of_an_expired_row_refuses_the_earlier_attempts_completion();
    }

    [Fact]
    public override Task cancel_of_a_claimed_idle_row_refuses_the_attempts_completion()
    {
        return base.cancel_of_a_claimed_idle_row_refuses_the_attempts_completion();
    }

    [Fact]
    public override Task delete_of_a_running_row_refuses_the_attempts_completion()
    {
        return base.delete_of_a_running_row_refuses_the_attempts_completion();
    }

    [Fact]
    public override Task edit_of_a_claimed_queued_row_refuses_the_attempts_completion()
    {
        return base.edit_of_a_claimed_queued_row_refuses_the_attempts_completion();
    }

    [Fact]
    public override Task dead_node_sweep_refuses_the_swept_attempts_time_job_completion()
    {
        return base.dead_node_sweep_refuses_the_swept_attempts_time_job_completion();
    }

    [Fact]
    public override Task stalled_reclaim_refuses_the_lapsed_attempts_time_job_completion()
    {
        return base.stalled_reclaim_refuses_the_lapsed_attempts_time_job_completion();
    }

    [Fact]
    public override Task dead_node_sweep_refuses_the_swept_attempts_occurrence_completion()
    {
        return base.dead_node_sweep_refuses_the_swept_attempts_occurrence_completion();
    }

    [Fact]
    public override Task stalled_reclaim_refuses_the_lapsed_attempts_occurrence_completion()
    {
        return base.stalled_reclaim_refuses_the_lapsed_attempts_occurrence_completion();
    }
}
