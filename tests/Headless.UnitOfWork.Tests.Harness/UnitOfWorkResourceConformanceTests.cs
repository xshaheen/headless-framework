// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Provider-portable acceptance scenarios for the unit-of-work contract against a real relational transaction
/// (PostgreSQL, SQL Server): owned commit/rollback, observed (caller-owned) commit/rollback, independent units,
/// the connection binding's join through <c>RunAsync</c>, and the refusal of a second begin on a bound connection. Every scenario proves its
/// outcome through <see cref="IUnitOfWorkResourceFixture.CountProbeRowsAsync" /> on an independent connection, not
/// through manager state alone. Scenarios that depend on scripted commit/rollback faults
/// (<see cref="FakeUnitOfWorkResource" />) stay in the in-memory <see cref="UnitOfWorkConformanceTests{TFixture}" />
/// suite — a real driver cannot be scripted to fault deterministically the same way.
/// </summary>
public abstract class UnitOfWorkResourceConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : IUnitOfWorkResourceFixture
{
    [Fact]
    public virtual async Task should_commit_probe_row_and_drain_on_completed_in_order_when_owned_unit_completes()
    {
        await fixture.ResetAsync(AbortToken);
        await using var session = fixture.CreateSession();
        await using var handle = await fixture.BeginOwnedAsync(session.Factory, AbortToken);
        var order = new List<int>();

        handle.UnitOfWork.OnCompleted(() =>
        {
            order.Add(1);

            return ValueTask.CompletedTask;
        });
        handle.UnitOfWork.OnCompleted(() =>
        {
            order.Add(2);

            return ValueTask.CompletedTask;
        });

        await fixture.InsertProbeRowAsync(handle.UnitOfWork, "owned-complete", AbortToken);
        await handle.UnitOfWork.CompleteAsync(AbortToken);

        order.Should().Equal(1, 2);
        handle.UnitOfWork.State.Should().Be(UnitOfWorkState.Completed);
        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1, "the committed probe row must be durable");
    }

    [Fact]
    public virtual async Task should_roll_back_probe_row_and_run_on_failed_abandoned_when_owned_unit_disposed_without_completing()
    {
        await fixture.ResetAsync(AbortToken);
        await using var session = fixture.CreateSession();
        UnitOfWorkFailure? failure = null;

        await using (var handle = await fixture.BeginOwnedAsync(session.Factory, AbortToken))
        {
            handle.UnitOfWork.OnFailed(f =>
            {
                failure = f;

                return ValueTask.CompletedTask;
            });

            await fixture.InsertProbeRowAsync(handle.UnitOfWork, "owned-abandon", AbortToken);
            // Disposed without a completion verb at the end of this block.
        }

        failure.Should().NotBeNull();
        failure!.Reason.Should().Be(UnitOfWorkFailureReason.Abandoned);
        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0, "the abandoned probe row must not be durable");
    }

    [Fact]
    public virtual async Task should_roll_back_probe_row_and_run_on_failed_rolled_back_when_owned_unit_is_rolled_back()
    {
        await fixture.ResetAsync(AbortToken);
        await using var session = fixture.CreateSession();
        await using var handle = await fixture.BeginOwnedAsync(session.Factory, AbortToken);
        UnitOfWorkFailure? failure = null;
        handle.UnitOfWork.OnFailed(f =>
        {
            failure = f;

            return ValueTask.CompletedTask;
        });

        await fixture.InsertProbeRowAsync(handle.UnitOfWork, "owned-rollback", AbortToken);
        await handle.UnitOfWork.RollbackAsync();

        failure.Should().NotBeNull();
        failure!.Reason.Should().Be(UnitOfWorkFailureReason.RolledBack);
        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0, "the rolled-back probe row must not be durable");
    }

    [Fact]
    public virtual async Task should_be_idempotent_when_owned_unit_rollback_is_called_twice()
    {
        await using var session = fixture.CreateSession();
        await using var handle = await fixture.BeginOwnedAsync(session.Factory, AbortToken);
        var failedCalls = 0;
        handle.UnitOfWork.OnFailed(_ =>
        {
            failedCalls++;

            return ValueTask.CompletedTask;
        });

        // A second RollbackAsync must not re-invoke the real driver's rollback on an already-completed
        // transaction — the driver would throw if it did.
        await handle.UnitOfWork.RollbackAsync();
        await handle.UnitOfWork.RollbackAsync();

        failedCalls.Should().Be(1);
        handle.UnitOfWork.State.Should().Be(UnitOfWorkState.Failed);
    }

    [Fact]
    public virtual async Task should_keep_two_owned_units_independent_when_each_commits_its_own_row()
    {
        // Two owned begins are two connections and two transactions: each commits its own row, and neither
        // sees the other's registrations or outcome. Nothing joins here because each began on its own connection.
        await fixture.ResetAsync(AbortToken);
        await using var session = fixture.CreateSession();
        await using var first = await fixture.BeginOwnedAsync(session.Factory, AbortToken);
        await using var second = await fixture.BeginOwnedAsync(session.Factory, AbortToken);
        var order = new List<string>();
        first.UnitOfWork.OnCompleted(() =>
        {
            order.Add("first");

            return ValueTask.CompletedTask;
        });
        second.UnitOfWork.OnCompleted(() =>
        {
            order.Add("second");

            return ValueTask.CompletedTask;
        });

        await fixture.InsertProbeRowAsync(first.UnitOfWork, "first-row", AbortToken);
        await fixture.InsertProbeRowAsync(second.UnitOfWork, "second-row", AbortToken);
        await second.UnitOfWork.CompleteAsync(AbortToken);

        order.Should().Equal("second");
        first.UnitOfWork.State.Should().Be(UnitOfWorkState.Active);

        await first.UnitOfWork.RollbackAsync();

        order.Should().Equal(["second"], "the rolled-back unit never drains its completions");
        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1, "only the completed unit's row is durable");
    }

    [Fact]
    public virtual async Task should_join_the_owners_unit_when_run_async_runs_on_a_bound_connection()
    {
        // A callee that wraps its own work in RunAsync(connection, …) under a caller that already began on that
        // connection joins the caller's unit: same handle, same transaction, and the commit stays with the owner.
        await fixture.ResetAsync(AbortToken);
        await using var session = fixture.CreateSession();
        await using var owner = await fixture.BeginOwnedAsync(session.Factory, AbortToken);
        IUnitOfWork? joined = null;

        await fixture.RunOnAsync(
            session.Factory,
            owner.Connection,
            async (unit, ct) =>
            {
                joined = unit;
                await fixture.InsertProbeRowAsync(unit, "joined-row", ct);
            },
            AbortToken
        );

        joined.Should().BeSameAs(owner.UnitOfWork);
        owner.Connection.UnitOfWork().Should().BeSameAs(owner.UnitOfWork);
        // No mid-transaction count here: the owner's uncommitted row would block an independent SQL Server
        // reader until timeout. The fault scenario below proves nothing is durable until the owner decides.
        owner
            .UnitOfWork.State.Should()
            .Be(UnitOfWorkState.Active, "the joined block must not complete the owner's unit");

        await owner.UnitOfWork.CompleteAsync(AbortToken);

        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1);
        owner.Connection.UnitOfWork().Should().BeNull("a terminal unit is evicted from the connection binding");
    }

    [Fact]
    public virtual async Task should_propagate_a_joined_blocks_fault_to_the_owner_and_leave_the_unit_to_it()
    {
        // The joined block owns nothing: its fault reaches the owner's block unchanged, and the unit is still the
        // owner's to roll back — which discards the joined block's row with everything else.
        await fixture.ResetAsync(AbortToken);
        await using var session = fixture.CreateSession();
        await using var owner = await fixture.BeginOwnedAsync(session.Factory, AbortToken);

        var act = () =>
            fixture.RunOnAsync(
                session.Factory,
                owner.Connection,
                async (unit, ct) =>
                {
                    await fixture.InsertProbeRowAsync(unit, "doomed", ct);
                    throw new InvalidOperationException("joined block failed");
                },
                AbortToken
            );

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("joined block failed");
        owner.UnitOfWork.State.Should().Be(UnitOfWorkState.Active, "only the owner decides the outcome");

        await owner.UnitOfWork.RollbackAsync();

        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0);
    }

    [Fact]
    public virtual async Task should_refuse_a_second_begin_on_a_connection_that_already_carries_a_live_unit()
    {
        // Two owning units cannot share one transaction; the refusal names the join and the hand-over.
        await fixture.ResetAsync(AbortToken);
        await using var session = fixture.CreateSession();
        await using var owner = await fixture.BeginOwnedAsync(session.Factory, AbortToken);

        var act = () => fixture.BeginOwnedOnAsync(session.Factory, owner.Connection, AbortToken).AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*already carries an active unit of work*RunAsync(connection*connection.UnitOfWork()*"
        );
        owner.UnitOfWork.State.Should().Be(UnitOfWorkState.Active, "a refused begin must not disturb the owner");
        owner.Connection.UnitOfWork().Should().BeSameAs(owner.UnitOfWork);
    }

    [Fact]
    public virtual async Task should_refuse_a_second_enlist_on_a_connection_that_already_carries_a_live_unit()
    {
        // Observing the owner's own transaction from a second unit is the same misuse as a second begin: two
        // units over one transaction. The enlist is refused before it registers anything.
        await fixture.ResetAsync(AbortToken);
        await using var session = fixture.CreateSession();
        await using var owner = await fixture.BeginOwnedAsync(session.Factory, AbortToken);
        var transaction = ((IRelationalUnitOfWorkResource)owner.UnitOfWork.Resource!).Transaction;

        var act = () => fixture.EnlistOn(session.Factory, owner.Connection, transaction);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*already carries an active unit of work*RunAsync(connection*connection.UnitOfWork()*");
        owner.UnitOfWork.State.Should().Be(UnitOfWorkState.Active, "a refused enlist must not disturb the owner");
        owner.Connection.UnitOfWork().Should().BeSameAs(owner.UnitOfWork);
    }

    [Fact]
    public virtual async Task should_evict_an_owned_unit_whose_transaction_ended_without_it()
    {
        // The transaction under an owned unit can end without the unit knowing (disposed by hand here). The
        // stale binding must not hand that unit to a later RunAsync, which would run its writes outside any
        // transaction and report success: the binding abandons the unit, and the next begin starts fresh.
        await fixture.ResetAsync(AbortToken);
        await using var session = fixture.CreateSession();
        await using var stale = await fixture.BeginOwnedAsync(session.Factory, AbortToken);

        await ((IRelationalUnitOfWorkResource)stale.UnitOfWork.Resource!).Transaction.DisposeAsync();

        stale.Connection.UnitOfWork().Should().BeNull("an owned unit whose transaction ended is evicted");
        stale.UnitOfWork.State.Should().Be(UnitOfWorkState.Failed, "the eviction abandons the unit");

        IUnitOfWork? fresh = null;
        await fixture.RunOnAsync(
            session.Factory,
            stale.Connection,
            async (unit, ct) =>
            {
                fresh = unit;
                await fixture.InsertProbeRowAsync(unit, "fresh", ct);
            },
            AbortToken
        );

        fresh.Should().NotBeSameAs(stale.UnitOfWork, "RunAsync begins a new unit instead of joining the stale one");
        (await fixture.CountProbeRowsAsync(AbortToken))
            .Should()
            .Be(1, "the fresh unit's own commit made the row durable");
    }

    [Fact]
    public virtual async Task should_drain_without_committing_when_an_observed_unit_completes_after_the_callers_commit()
    {
        await fixture.ResetAsync(AbortToken);
        using var logs = new CapturingLoggerProvider();
        await using var session = fixture.CreateSession(logs);
        await using var handle = await fixture.EnlistObservedAsync(session.Factory, AbortToken);
        var calls = 0;
        handle.UnitOfWork.OnCompleted(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await fixture.InsertProbeRowAsync(handle.UnitOfWork, "observed-complete", AbortToken);
        await handle.CommitTransactionAsync(AbortToken);
        await handle.UnitOfWork.CompleteAsync(AbortToken);

        calls.Should().Be(1);
        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1, "the caller's own commit made the row durable");
        logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public virtual async Task should_log_the_forgotten_completion_warning_when_an_observed_unit_is_disposed_without_a_verb_after_the_callers_commit()
    {
        await fixture.ResetAsync(AbortToken);
        using var logs = new CapturingLoggerProvider();
        await using var session = fixture.CreateSession(logs);
        IUnitOfWork unit;

        await using (var handle = await fixture.EnlistObservedAsync(session.Factory, AbortToken))
        {
            unit = handle.UnitOfWork;
            await fixture.InsertProbeRowAsync(handle.UnitOfWork, "observed-forgotten", AbortToken);
            await handle.CommitTransactionAsync(AbortToken);
            // Forgotten completion: disposed without CompleteAsync or RollbackAsync after the caller's own commit.
        }

        unit.State.Should().Be(UnitOfWorkState.Failed);
        (await fixture.CountProbeRowsAsync(AbortToken))
            .Should()
            .Be(1, "the caller's own commit already made the row durable");

        var warning = logs.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain("disposed without CompleteAsync or RollbackAsync");
    }

    [Fact]
    public virtual async Task should_not_log_the_forgotten_completion_warning_when_the_callers_rollback_is_followed_by_an_explicit_rollback()
    {
        await fixture.ResetAsync(AbortToken);
        using var logs = new CapturingLoggerProvider();
        await using var session = fixture.CreateSession(logs);
        await using var handle = await fixture.EnlistObservedAsync(session.Factory, AbortToken);
        UnitOfWorkFailure? failure = null;
        handle.UnitOfWork.OnFailed(f =>
        {
            failure = f;

            return ValueTask.CompletedTask;
        });

        await fixture.InsertProbeRowAsync(handle.UnitOfWork, "observed-rollback", AbortToken);
        await handle.RollbackTransactionAsync(AbortToken);
        await handle.UnitOfWork.RollbackAsync();

        failure.Should().NotBeNull();
        failure!.Reason.Should().Be(UnitOfWorkFailureReason.RolledBack);
        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0, "the caller's own rollback discarded the row");
        logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }
}
