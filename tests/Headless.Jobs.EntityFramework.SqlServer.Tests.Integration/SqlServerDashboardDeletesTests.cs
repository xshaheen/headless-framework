// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the dashboard-deletes conformance suite against SqlServer.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerDashboardDeletesTests(SqlServerJobsCoordinationFixture fixture)
    : JobsDashboardDeletesConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task a_force_delete_removes_a_running_job_with_cancel_requested()
    {
        return base.a_force_delete_removes_a_running_job_with_cancel_requested();
    }

    [Fact]
    public override Task a_force_delete_keeps_a_running_job_whose_cancel_was_not_requested()
    {
        return base.a_force_delete_keeps_a_running_job_whose_cancel_was_not_requested();
    }

    [Fact]
    public override Task a_force_delete_keeps_a_job_that_finished_after_it_was_read()
    {
        return base.a_force_delete_keeps_a_job_that_finished_after_it_was_read();
    }

    [Fact]
    public override Task a_cron_delete_removes_the_job_with_its_twelve_stored_occurrences()
    {
        return base.a_cron_delete_removes_the_job_with_its_twelve_stored_occurrences();
    }

    [Fact]
    public override Task a_cron_delete_keeps_the_job_and_its_occurrences_when_one_started_after_the_read()
    {
        return base.a_cron_delete_keeps_the_job_and_its_occurrences_when_one_started_after_the_read();
    }
}
