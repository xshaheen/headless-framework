// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Runs the coordinated-transaction helper conformance suite against the EF Core helper over SQLite.
/// The commit signal arrives through <c>CommitCoordinationTransactionInterceptor</c>
/// (awaited inside <c>CommitAsync</c>).
/// </summary>
public sealed class EfCoordinatedTransactionConformanceTests(EfCoordinatedTransactionFixture fixture)
    : CoordinatedTransactionConformanceTests<EfCoordinatedTransactionFixture>(fixture),
        IClassFixture<EfCoordinatedTransactionFixture>
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
