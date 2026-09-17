// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Runs the <c>RunAsync</c> conformance suite against the raw-ADO PostgreSQL helper.</summary>
[Collection<PostgreSqlUnitOfWorkFixture>]
public sealed class PostgreSqlUnitOfWorkRunConformanceTests(PostgreSqlUnitOfWorkFixture fixture)
    : UnitOfWorkRunConformanceTests<PostgreSqlUnitOfWorkFixture>(fixture)
{
    [Fact]
    public override Task should_drain_completion_work_and_persist_rows_when_operation_completes()
    {
        return base.should_drain_completion_work_and_persist_rows_when_operation_completes();
    }

    [Fact]
    public override Task should_discard_completion_work_and_roll_back_rows_when_operation_throws()
    {
        return base.should_discard_completion_work_and_roll_back_rows_when_operation_throws();
    }
}
