// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the dashboard paging conformance suite against SqlServer.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerDashboardPagingTests(SqlServerJobsCoordinationFixture fixture)
    : JobsDashboardPagingConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task time_job_pages_over_one_execution_time_visit_every_root_once()
    {
        return base.time_job_pages_over_one_execution_time_visit_every_root_once();
    }

    [Fact]
    public override Task cron_job_pages_over_one_creation_time_visit_every_definition_once()
    {
        return base.cron_job_pages_over_one_creation_time_visit_every_definition_once();
    }

    [Fact]
    public override Task run_list_pages_sorted_by_function_or_status_visit_every_tied_run_once_in_one_order()
    {
        return base.run_list_pages_sorted_by_function_or_status_visit_every_tied_run_once_in_one_order();
    }
}
