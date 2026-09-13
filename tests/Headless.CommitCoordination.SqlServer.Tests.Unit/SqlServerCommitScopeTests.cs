// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// The explicit-signal contract of a directly enlisted SQL Server transaction, isolated from SqlClient: the
/// transaction's completion is a delegate, so the forgotten-signal warning is asserted without a server.
/// </summary>
public sealed class SqlServerCommitScopeTests : TestBase
{
    [Fact]
    public async Task should_warn_and_discard_when_disposed_without_a_signal_after_the_transaction_completed()
    {
        var harness = new Harness(transactionCompleted: true);
        var calls = 0;

        harness.Scope.Coordinator.OnCommit(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await harness.Scope.DisposeAsync();

        calls.Should().Be(0);
        harness.Scope.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
        harness
            .Logger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Warning && e.EventId.Id == 1)
            .Which.Message.Should()
            .Contain("disposed without a signal");
        harness.Stack.Current.Should().BeNull();
    }

    [Fact]
    public void should_warn_on_the_synchronous_dispose_path_too()
    {
        var harness = new Harness(transactionCompleted: true);

        harness.Scope.Dispose();

        harness.Logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
        harness.Scope.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
    }

    [Fact]
    public async Task should_not_warn_when_disposed_without_a_signal_while_the_transaction_is_still_open()
    {
        // The operation threw before commit: the transaction rolls back when it disposes, and the un-signalled
        // scope dispose is the normal discard path, not a forgotten signal.
        var harness = new Harness(transactionCompleted: false);

        await harness.Scope.DisposeAsync();

        harness.Scope.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
        harness.Logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_drain_and_stay_silent_when_the_caller_signals_committed_before_disposing()
    {
        var harness = new Harness(transactionCompleted: true);
        var calls = 0;

        harness.Scope.Coordinator.OnCommit(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await harness.Scope.SignalAsync(CommitOutcome.Committed);
        await harness.Scope.DisposeAsync();

        calls.Should().Be(1);
        harness.Scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
        harness.Logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_stay_silent_when_the_caller_signals_rolled_back_before_disposing()
    {
        var harness = new Harness(transactionCompleted: true);

        await harness.Scope.SignalAsync(CommitOutcome.RolledBack);
        await harness.Scope.DisposeAsync();

        harness.Scope.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
        harness.Logger.Entries.Should().BeEmpty();
    }

    private sealed class Harness
    {
        public CommitScopeStack Stack { get; } = new();

        public CapturingLogger<SqlServerCommitScope> Logger { get; } = new();

        public ICommitScope Scope { get; }

        public Harness(bool transactionCompleted)
        {
            var inner = new CommitScopeFactory(Stack).Open(new StubRelationalCommitContext());
            Scope = new SqlServerCommitScope(inner, () => transactionCompleted, Logger);
        }
    }
}
