// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the persisted scheduling trace context conformance suite against PostgreSQL.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlTraceParentTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsTraceParentConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task time_job_trace_parent_survives_insert_update_and_claim()
    {
        return base.time_job_trace_parent_survives_insert_update_and_claim();
    }

    [Fact]
    public override Task requeue_replaces_the_stored_trace_parent()
    {
        return base.requeue_replaces_the_stored_trace_parent();
    }

    [Fact]
    public override Task on_demand_occurrence_keeps_its_trace_parent_and_a_materialized_one_has_none()
    {
        return base.on_demand_occurrence_keeps_its_trace_parent_and_a_materialized_one_has_none();
    }
}
