// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the change-marker conformance suite against PostgreSql.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlChangeMarkerTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsChangeMarkerConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task completion_write_stamps_the_database_clock_and_raises_the_marker()
    {
        return base.completion_write_stamps_the_database_clock_and_raises_the_marker();
    }

    [Fact]
    public override Task back_to_back_writes_to_one_row_produce_strictly_increasing_markers()
    {
        return base.back_to_back_writes_to_one_row_produce_strictly_increasing_markers();
    }

    [Fact]
    public override Task write_after_the_clock_steps_back_still_raises_the_marker()
    {
        return base.write_after_the_clock_steps_back_still_raises_the_marker();
    }

    [Fact]
    public override Task time_job_add_and_claims_stamp_the_marker()
    {
        return base.time_job_add_and_claims_stamp_the_marker();
    }

    [Fact]
    public override Task cron_occurrence_claims_stamp_the_marker()
    {
        return base.cron_occurrence_claims_stamp_the_marker();
    }

    [Fact]
    public override Task progress_writes_stamp_the_marker()
    {
        return base.progress_writes_stamp_the_marker();
    }

    [Fact]
    public override Task retry_success_and_failure_writes_stamp_the_marker()
    {
        return base.retry_success_and_failure_writes_stamp_the_marker();
    }

    [Fact]
    public override Task cancel_and_requeue_stamp_the_marker()
    {
        return base.cancel_and_requeue_stamp_the_marker();
    }

    [Fact]
    public override Task deleting_a_child_stamps_the_surviving_parent()
    {
        return base.deleting_a_child_stamps_the_surviving_parent();
    }

    [Fact]
    public override Task editing_a_time_job_stamps_the_marker()
    {
        return base.editing_a_time_job_stamps_the_marker();
    }

    [Fact]
    public override Task cron_add_pause_and_resume_stamp_the_definition_marker()
    {
        return base.cron_add_pause_and_resume_stamp_the_definition_marker();
    }

    [Fact]
    public override Task change_read_returns_rows_in_marker_order_with_the_next_cursor()
    {
        return base.change_read_returns_rows_in_marker_order_with_the_next_cursor();
    }

    [Fact]
    public override Task writes_committed_out_of_stamp_order_are_both_read()
    {
        return base.writes_committed_out_of_stamp_order_are_both_read();
    }

    [Fact]
    public override Task a_burst_larger_than_a_page_inside_the_overlap_does_not_hide_a_later_write()
    {
        return base.a_burst_larger_than_a_page_inside_the_overlap_does_not_hide_a_later_write();
    }

    [Fact]
    public override Task enlisted_time_job_held_open_past_the_window_reaches_another_nodes_change_read()
    {
        return base.enlisted_time_job_held_open_past_the_window_reaches_another_nodes_change_read();
    }
}
