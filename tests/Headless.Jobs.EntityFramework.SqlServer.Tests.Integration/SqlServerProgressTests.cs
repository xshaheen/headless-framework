// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the stored job progress conformance suite against SqlServer.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerProgressTests(SqlServerJobsCoordinationFixture fixture)
    : JobsProgressConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task time_job_progress_is_fenced_to_the_owning_node_and_read_back_by_any_node()
    {
        return base.time_job_progress_is_fenced_to_the_owning_node_and_read_back_by_any_node();
    }

    [Fact]
    public override Task cron_occurrence_progress_is_fenced_to_the_owning_node_and_read_back_by_any_node()
    {
        return base.cron_occurrence_progress_is_fenced_to_the_owning_node_and_read_back_by_any_node();
    }

    [Fact]
    public override Task progress_writes_stamp_the_database_clock_not_the_node_clock()
    {
        return base.progress_writes_stamp_the_database_clock_not_the_node_clock();
    }

    [Fact]
    public override Task progress_writes_nothing_while_membership_is_not_established()
    {
        return base.progress_writes_nothing_while_membership_is_not_established();
    }

    [Fact]
    public override Task terminal_writes_store_the_report_the_throttle_had_not_written()
    {
        return base.terminal_writes_store_the_report_the_throttle_had_not_written();
    }

    [Fact]
    public override Task crash_recovery_keeps_progress_and_a_requeue_clears_it()
    {
        return base.crash_recovery_keeps_progress_and_a_requeue_clears_it();
    }

    [Fact]
    public override Task a_requeued_cron_occurrence_clears_its_progress()
    {
        return base.a_requeued_cron_occurrence_clears_its_progress();
    }
}
