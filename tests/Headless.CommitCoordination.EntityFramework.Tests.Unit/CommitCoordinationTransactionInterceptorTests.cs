// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.CommitCoordination;
using Headless.CommitCoordination.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Exercises the interceptor's owned transaction-to-scope map against a fake <see cref="DbTransaction" />: the
/// claim/drain threading on the sync edge, eviction on the terminal edge and on scope dispose, re-enlistment of a
/// transaction whose previous scope already settled, and the inbox-runner shape where the caller signals the scope
/// explicitly alongside — or instead of — the interceptor.
/// </summary>
public sealed class CommitCoordinationTransactionInterceptorTests : TestBase
{
    [Fact]
    public async Task should_not_deadlock_when_sync_commit_override_drains_under_a_synchronization_context()
    {
        var harness = new Harness();
        var transaction = new FakeDbTransaction();
        var ran = false;

        await using var scope = harness.Enlist(transaction);

        scope.Coordinator.OnCommit(async () =>
        {
            // Posts the continuation back to the captured SynchronizationContext; the sync interceptor override
            // would deadlock here unless it offloads the drain off the committing thread.
            await Task.Yield();
            ran = true;
        });

        var completed = SingleThreadSynchronizationContext.Run(
            () => harness.Interceptor.TransactionCommitted(transaction, null!),
            TimeSpan.FromSeconds(10)
        );

        completed
            .Should()
            .BeTrue(
                "the sync TransactionCommitted override must offload the drain off the captured SynchronizationContext"
            );
        SpinWait.SpinUntil(() => ran, TimeSpan.FromSeconds(5)).Should().BeTrue();
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public void should_throw_and_keep_the_first_scope_ambient_when_a_transaction_is_enlisted_twice()
    {
        var harness = new Harness();
        var transaction = new FakeDbTransaction();

        using var first = harness.Enlist(transaction);

        harness
            .Invoking(h => h.Enlist(transaction))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("An EF Core commit coordination scope is already enlisted for this transaction.");

        harness.Interceptor.EnlistedTransactionCount.Should().Be(1);
        harness.Stack.Current.Should().BeSameAs(first.Coordinator, "the rejected duplicate must pop its own frame");
    }

    [Fact]
    public async Task should_evict_the_map_entry_on_the_async_commit_edge_while_the_scope_is_still_alive()
    {
        var harness = new Harness();
        var transaction = new FakeDbTransaction();
        await using var scope = harness.Enlist(transaction);

        await harness.Interceptor.TransactionCommittedAsync(transaction, null!, AbortToken);

        harness
            .Interceptor.EnlistedTransactionCount.Should()
            .Be(0, "a durable outcome is finished work and must not pin the key until the scope is disposed");
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public void should_evict_the_map_entry_on_the_sync_rollback_edge_while_the_scope_is_still_alive()
    {
        var harness = new Harness();
        var transaction = new FakeDbTransaction();
        using var scope = harness.Enlist(transaction);

        harness.Interceptor.TransactionRolledBack(transaction, null!);

        harness.Interceptor.EnlistedTransactionCount.Should().Be(0);
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
    }

    [Fact]
    public async Task should_replace_a_finished_scope_when_the_same_transaction_is_enlisted_again()
    {
        // Npgsql hands out one NpgsqlTransaction instance per connection: the next transaction re-enlists the same
        // key while the previous scope, settled by a caller-confirmed commit that raised no edge, awaits its dispose.
        var harness = new Harness();
        var transaction = new FakeDbTransaction();
        var first = harness.Enlist(transaction);

        await first.SignalAsync(CommitOutcome.Committed);
        harness.Interceptor.EnlistedTransactionCount.Should().Be(1, "no edge fired, so nothing evicted the entry");

        var second = harness.Enlist(transaction);

        harness.Interceptor.EnlistedTransactionCount.Should().Be(1);
        second.Coordinator.Should().NotBeSameAs(first.Coordinator);
        second.Coordinator.State.Should().Be(CommitCoordinatorState.Active);
        harness.Stack.Current.Should().BeSameAs(second.Coordinator);
        harness.InterceptorLogger.Entries.Should().BeEmpty("finished work is not a duplicate enlistment");

        // Unwind in order so the ambient frame does not leak into the async flow.
        await second.DisposeAsync();
        await first.DisposeAsync();

        harness.Interceptor.EnlistedTransactionCount.Should().Be(0);
    }

    [Fact]
    public async Task should_keep_the_map_entry_when_an_out_of_order_dispose_is_rejected()
    {
        var harness = new Harness();
        var outerTransaction = new FakeDbTransaction();
        var innerTransaction = new FakeDbTransaction();
        var outer = harness.Enlist(outerTransaction);
        var inner = harness.Enlist(innerTransaction);

        var act = () => outer.DisposeAsync().AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Commit scope disposed out of order.");
        harness
            .Interceptor.EnlistedTransactionCount.Should()
            .Be(2, "a scope whose dispose was rejected is still live and must stay reachable on its edge");
        outer.Coordinator.State.Should().Be(CommitCoordinatorState.Active);

        // Unwind in order so the ambient frame does not leak into the async flow.
        await inner.DisposeAsync();
        await outer.DisposeAsync();

        harness.Interceptor.EnlistedTransactionCount.Should().Be(0);
    }

    [Fact]
    public async Task should_evict_the_map_entry_when_the_scope_is_disposed_without_any_interceptor_event()
    {
        var harness = new Harness();
        var transaction = new FakeDbTransaction();
        var scope = harness.Enlist(transaction);

        harness.Interceptor.EnlistedTransactionCount.Should().Be(1);

        await scope.DisposeAsync();

        harness.Interceptor.EnlistedTransactionCount.Should().Be(0, "eviction is owned by the scope, not the edge");
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack, "an un-signalled dispose rolls back");
        harness.Stack.Current.Should().BeNull();
    }

    [Fact]
    public async Task should_drain_once_with_no_warning_when_the_interceptor_and_the_caller_both_signal_committed()
    {
        var harness = new Harness();
        var transaction = new FakeDbTransaction();
        var calls = 0;
        var scope = harness.Enlist(transaction);

        scope.Coordinator.OnCommit(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        // The commit edge fires first (as it does inside CommitAsync); the runner then settles the outcome itself.
        await harness.Interceptor.TransactionCommittedAsync(transaction, null!, AbortToken);
        await scope.SignalAsync(CommitOutcome.Committed);
        await scope.DisposeAsync();

        calls.Should().Be(1);
        harness.CoordinatorLogger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
        harness.Interceptor.EnlistedTransactionCount.Should().Be(0);
    }

    [Fact]
    public async Task should_drain_once_and_leave_the_map_empty_when_the_caller_confirms_a_commit_the_interceptor_never_observed()
    {
        // A commit that throws client-side raises no interceptor event; the runner probes the store, finds the row
        // committed, and signals the scope directly. Nothing else may be required to keep the map clean.
        var harness = new Harness();
        var transaction = new FakeDbTransaction();
        var calls = 0;
        var scope = harness.Enlist(transaction);

        scope.Coordinator.OnCommit(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await scope.SignalAsync(CommitOutcome.Committed);
        await scope.DisposeAsync();

        calls.Should().Be(1);
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
        harness.Interceptor.EnlistedTransactionCount.Should().Be(0);
        harness.CoordinatorLogger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task should_ignore_edges_for_transactions_that_were_never_enlisted()
    {
        var harness = new Harness();
        var transaction = new FakeDbTransaction();

        harness.Interceptor.TransactionCommitted(transaction, null!);
        harness.Interceptor.TransactionRolledBack(transaction, null!);
        await harness.Interceptor.TransactionCommittedAsync(transaction, null!, AbortToken);
        await harness.Interceptor.TransactionRolledBackAsync(transaction, null!, AbortToken);

        harness.InterceptorLogger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_log_and_swallow_a_drain_fault_on_the_async_commit_edge()
    {
        var harness = new Harness();
        var transaction = new FakeDbTransaction();
        await using var scope = harness.Enlist(transaction);

        scope.Coordinator.OnCommit(() => throw new InvalidOperationException("callback-fault"));

        var act = async () => await harness.Interceptor.TransactionCommittedAsync(transaction, null!, AbortToken);

        await act.Should()
            .NotThrowAsync("the commit is already durable; a drain fault must not become a phantom failure");
        harness.InterceptorLogger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.EventId.Id == 2);
    }

    private sealed class Harness
    {
        public CommitScopeStack Stack { get; } = new();

        public CapturingLogger<CommitCoordinator> CoordinatorLogger { get; } = new();

        public CapturingLogger<CommitCoordinationTransactionInterceptor> InterceptorLogger { get; } = new();

        public CommitScopeFactory Factory { get; }

        public CommitCoordinationTransactionInterceptor Interceptor { get; }

        public Harness()
        {
            Factory = new CommitScopeFactory(Stack, CoordinatorLogger);
            Interceptor = new CommitCoordinationTransactionInterceptor(InterceptorLogger);
        }

        public ICommitScope Enlist(DbTransaction transaction)
        {
            return Interceptor.Enlist(Factory, new StubRelationalCommitContext(), transaction);
        }
    }

    private sealed class FakeDbTransaction : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection? DbConnection => null;

        public override void Commit() { }

        public override void Rollback() { }
    }
}
