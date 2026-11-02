// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the time-job peek conformance suite against PostgreSQL.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlPeekTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsPeekConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Theory]
    [InlineData(5)]
    [InlineData(-5)]
    public override Task peek_returns_a_row_due_on_the_store_clock_whatever_the_node_clock_says(int nodeSkewSeconds)
    {
        return base.peek_returns_a_row_due_on_the_store_clock_whatever_the_node_clock_says(nodeSkewSeconds);
    }

    [Fact]
    public override Task peek_returns_an_absolute_time_a_lagging_node_set_to_its_own_now()
    {
        return base.peek_returns_an_absolute_time_a_lagging_node_set_to_its_own_now();
    }

    [Theory]
    [InlineData(120)]
    [InlineData(-120)]
    public override Task peek_returns_a_run_now_row_a_skewed_node_enqueued(int nodeSkewSeconds)
    {
        return base.peek_returns_a_run_now_row_a_skewed_node_enqueued(nodeSkewSeconds);
    }

    [Fact]
    public override Task immediate_acquire_takes_only_rows_due_on_the_store_clock()
    {
        return base.immediate_acquire_takes_only_rows_due_on_the_store_clock();
    }

    [Fact]
    public override Task peek_skips_a_row_this_node_holds_and_returns_a_row_whose_foreign_lease_lapsed()
    {
        return base.peek_skips_a_row_this_node_holds_and_returns_a_row_whose_foreign_lease_lapsed();
    }

    [Fact]
    public override Task cron_occurrence_peek_skips_an_occurrence_this_node_holds_and_returns_one_whose_foreign_lease_lapsed()
    {
        return base.cron_occurrence_peek_skips_an_occurrence_this_node_holds_and_returns_one_whose_foreign_lease_lapsed();
    }
}
