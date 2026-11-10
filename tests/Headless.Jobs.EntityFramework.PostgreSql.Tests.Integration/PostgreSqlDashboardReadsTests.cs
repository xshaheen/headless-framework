// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the dashboard-reads conformance suite against PostgreSql.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlDashboardReadsTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsDashboardReadsConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task the_run_list_pages_both_kinds_in_execution_time_order_with_no_repeat_or_gap()
    {
        return base.the_run_list_pages_both_kinds_in_execution_time_order_with_no_repeat_or_gap();
    }

    [Fact]
    public override Task a_count_over_ten_thousand_rows_stops_at_ten_thousand_and_reports_capped()
    {
        return base.a_count_over_ten_thousand_rows_stops_at_ten_thousand_and_reports_capped();
    }

    [Fact]
    public override Task a_failed_occurrence_leaves_its_group_once_a_later_one_succeeds_and_a_failed_time_job_stays_until_requeued()
    {
        return base.a_failed_occurrence_leaves_its_group_once_a_later_one_succeeds_and_a_failed_time_job_stays_until_requeued();
    }

    [Fact]
    public override Task a_superseded_keyed_job_and_a_chain_member_are_not_actionable()
    {
        return base.a_superseded_keyed_job_and_a_chain_member_are_not_actionable();
    }

    [Fact]
    public override Task a_requeue_scope_leaves_out_a_run_changed_after_the_high_water_mark()
    {
        return base.a_requeue_scope_leaves_out_a_run_changed_after_the_high_water_mark();
    }

    [Fact]
    public override Task a_cancel_scope_keeps_a_running_run_that_reported_progress_after_confirm()
    {
        return base.a_cancel_scope_keeps_a_running_run_that_reported_progress_after_confirm();
    }

    [Fact]
    public override Task a_delete_applies_only_while_the_marker_is_unchanged()
    {
        return base.a_delete_applies_only_while_the_marker_is_unchanged();
    }

    [Fact]
    public override Task the_overview_figures_count_the_last_24_hours_and_the_24_hours_before()
    {
        return base.the_overview_figures_count_the_last_24_hours_and_the_24_hours_before();
    }

    [Fact]
    public override Task the_trace_list_and_the_id_reads_return_the_runs_they_name()
    {
        return base.the_trace_list_and_the_id_reads_return_the_runs_they_name();
    }

    [Fact]
    public override Task per_function_stats_count_both_kinds()
    {
        return base.per_function_stats_count_both_kinds();
    }

    [Fact]
    public override Task a_search_matches_function_id_business_key_and_description_literally_ignoring_case()
    {
        return base.a_search_matches_function_id_business_key_and_description_literally_ignoring_case();
    }

    [Fact]
    public override Task a_run_serves_its_progress_time_only_once_it_reports_and_its_retry_settings()
    {
        return base.a_run_serves_its_progress_time_only_once_it_reports_and_its_retry_settings();
    }

    [Fact]
    public override Task bucketed_counts_place_each_run_by_execution_time_and_count_an_empty_bucket_as_zero()
    {
        return base.bucketed_counts_place_each_run_by_execution_time_and_count_an_empty_bucket_as_zero();
    }

    [Fact]
    public override Task in_progress_runs_are_counted_per_claim_owner()
    {
        return base.in_progress_runs_are_counted_per_claim_owner();
    }
}
