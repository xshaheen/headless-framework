// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Runs the cron overlap-policy conformance suite against SQL Server.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerOverlapTests(SqlServerJobsCoordinationFixture fixture)
    : JobsOverlapConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task skip_records_the_due_occurrence_as_skipped_while_an_earlier_one_is_in_progress()
    {
        return base.skip_records_the_due_occurrence_as_skipped_while_an_earlier_one_is_in_progress();
    }

    [Fact]
    public override Task skip_treats_an_idle_retry_as_unfinished()
    {
        return base.skip_treats_an_idle_retry_as_unfinished();
    }

    [Fact]
    public override Task skip_materializes_normally_once_the_earlier_occurrence_finished()
    {
        return base.skip_materializes_normally_once_the_earlier_occurrence_finished();
    }

    [Fact]
    public override Task allow_materializes_alongside_an_in_progress_occurrence()
    {
        return base.allow_materializes_alongside_an_in_progress_occurrence();
    }

    [Fact]
    public override Task concurrent_nodes_record_one_skipped_occurrence_and_no_live_one()
    {
        return base.concurrent_nodes_record_one_skipped_occurrence_and_no_live_one();
    }

    [Fact]
    public override Task recovery_skips_its_run_while_an_execution_from_before_the_outage_is_unfinished()
    {
        return base.recovery_skips_its_run_while_an_execution_from_before_the_outage_is_unfinished();
    }

    [Fact]
    public override Task seeding_applies_the_overlap_policy_only_at_creation()
    {
        return base.seeding_applies_the_overlap_policy_only_at_creation();
    }

    [Fact]
    public override Task schedule_edit_does_not_pre_create_a_replacement_next_to_a_running_occurrence()
    {
        return base.schedule_edit_does_not_pre_create_a_replacement_next_to_a_running_occurrence();
    }

    [Fact]
    public override Task materialization_retires_a_pre_created_idle_occurrence_next_to_a_running_one()
    {
        return base.materialization_retires_a_pre_created_idle_occurrence_next_to_a_running_one();
    }
}
