// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Runs the cron schedule-position advance conformance suite against SQL Server.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerSchedulePositionTests(SqlServerJobsCoordinationFixture fixture)
    : JobsSchedulePositionConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task advance_from_the_observed_watermark_persists_the_new_position()
    {
        return base.advance_from_the_observed_watermark_persists_the_new_position();
    }

    [Fact]
    public override Task advance_with_a_stale_watermark_changes_nothing()
    {
        return base.advance_with_a_stale_watermark_changes_nothing();
    }

    [Fact]
    public override Task advance_with_a_stale_schedule_revision_changes_nothing()
    {
        return base.advance_with_a_stale_schedule_revision_changes_nothing();
    }

    [Fact]
    public override Task advance_against_a_paused_definition_changes_nothing()
    {
        return base.advance_against_a_paused_definition_changes_nothing();
    }

    [Fact]
    public override Task concurrent_advances_from_the_same_watermark_produce_exactly_one_winner()
    {
        return base.concurrent_advances_from_the_same_watermark_produce_exactly_one_winner();
    }

    [Fact]
    public override Task advancing_one_definition_leaves_a_sibling_untouched()
    {
        return base.advancing_one_definition_leaves_a_sibling_untouched();
    }

    [Fact]
    public override Task due_ness_and_the_returned_instant_follow_the_database_clock_not_a_skewed_node_clock()
    {
        return base.due_ness_and_the_returned_instant_follow_the_database_clock_not_a_skewed_node_clock();
    }
}
