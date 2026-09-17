// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Runs the <c>RunAsync</c> conformance suite against the raw-ADO SQL Server helper.</summary>
[Collection<SqlServerUnitOfWorkFixture>]
public sealed class SqlServerUnitOfWorkRunConformanceTests(SqlServerUnitOfWorkFixture fixture)
    : UnitOfWorkRunConformanceTests<SqlServerUnitOfWorkFixture>(fixture)
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
