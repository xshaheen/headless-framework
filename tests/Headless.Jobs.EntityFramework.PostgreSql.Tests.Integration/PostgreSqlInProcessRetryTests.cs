// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the in-process retry conformance suite against PostgreSql.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlInProcessRetryTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsInProcessRetryConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task in_process_retries_spend_the_budget_and_end_the_run_failed()
    {
        return base.in_process_retries_spend_the_budget_and_end_the_run_failed();
    }
}
