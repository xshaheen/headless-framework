// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Runs the coordinated-transaction helper conformance suite against the raw-ADO PostgreSQL helper.
/// PostgreSQL is the inline (caller-driven) provider: the helper itself signals <c>Committed</c> after
/// <c>CommitAsync</c> — these scenarios are the regression guard for the silent-work-loss P0.
/// </summary>
[Collection<PostgreSqlCoordinatedTransactionFixture>]
public sealed class PostgreSqlCoordinatedTransactionConformanceTests(PostgreSqlCoordinatedTransactionFixture fixture)
    : CoordinatedTransactionConformanceTests<PostgreSqlCoordinatedTransactionFixture>(fixture)
{
    [Fact]
    public override Task should_drain_buffered_commit_work_and_persist_rows_when_operation_commits()
    {
        return base.should_drain_buffered_commit_work_and_persist_rows_when_operation_commits();
    }

    [Fact]
    public override Task should_discard_buffered_commit_work_and_roll_back_rows_when_operation_throws()
    {
        return base.should_discard_buffered_commit_work_and_roll_back_rows_when_operation_throws();
    }
}
