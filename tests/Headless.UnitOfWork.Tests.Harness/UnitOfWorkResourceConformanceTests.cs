// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Provider-portable acceptance scenarios for the unit-of-work contract against a real relational transaction
/// (PostgreSQL, SQL Server): owned commit/rollback, observed (caller-owned) commit/rollback, join-by-default
/// nesting on the same resource, resource-conflict rejection, and scope-leak recovery. Every scenario proves its
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
        await using var handle = await fixture.BeginOwnedAsync(session.Manager, AbortToken);
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

        await using (var handle = await fixture.BeginOwnedAsync(session.Manager, AbortToken))
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
        await using var handle = await fixture.BeginOwnedAsync(session.Manager, AbortToken);
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
        await using var handle = await fixture.BeginOwnedAsync(session.Manager, AbortToken);
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
    public virtual async Task should_transfer_child_registrations_to_the_root_and_commit_once_when_child_of_the_same_resource_completes()
    {
        await fixture.ResetAsync(AbortToken);
        await using var session = fixture.CreateSession();
        await using var root = await fixture.BeginOwnedAsync(session.Manager, AbortToken);
        var order = new List<string>();
        root.UnitOfWork.OnCompleted(() =>
        {
            order.Add("root");

            return ValueTask.CompletedTask;
        });

        var resource = root.UnitOfWork.Resource ?? throw new InvalidOperationException("The root exposed no resource.");

        await using (
            var child = await session.Manager.BeginAsync(
                _ => ValueTask.FromResult(resource),
                options: null,
                cancellationToken: AbortToken
            )
        )
        {
            child.OnCompleted(() =>
            {
                order.Add("child");

                return ValueTask.CompletedTask;
            });

            await fixture.InsertProbeRowAsync(child, "child-complete", AbortToken);
            await child.CompleteAsync(AbortToken);
        }

        order.Should().BeEmpty("a child completion defers its registrations to the root");
        session.Manager.Current.Should().BeSameAs(root.UnitOfWork);

        await root.UnitOfWork.CompleteAsync(AbortToken);

        order.Should().Equal("root", "child");
        (await fixture.CountProbeRowsAsync(AbortToken))
            .Should()
            .Be(1, "the root's single commit persisted the child's row");
    }

    [Fact]
    public virtual async Task should_abort_the_root_and_roll_back_the_probe_row_when_a_child_of_the_same_resource_is_abandoned()
    {
        await fixture.ResetAsync(AbortToken);
        await using var session = fixture.CreateSession();
        var root = await fixture.BeginOwnedAsync(session.Manager, AbortToken);
        var resource = root.UnitOfWork.Resource ?? throw new InvalidOperationException("The root exposed no resource.");

        await fixture.InsertProbeRowAsync(root.UnitOfWork, "root-row", AbortToken);

        var child = await session.Manager.BeginAsync(
            _ => ValueTask.FromResult(resource),
            options: null,
            cancellationToken: AbortToken
        );
        var childCompletedCalls = 0;
        child.OnCompleted(() =>
        {
            childCompletedCalls++;

            return ValueTask.CompletedTask;
        });

        await child.DisposeAsync();

        childCompletedCalls.Should().Be(0, "an abandoned child's registrations are dropped, not transferred");
        session.Manager.Current.Should().BeSameAs(root.UnitOfWork);
        root.UnitOfWork.State.Should().Be(UnitOfWorkState.Failed);
        root.UnitOfWork.Failure!.Reason.Should().Be(UnitOfWorkFailureReason.ChildAbandoned);
        (await fixture.CountProbeRowsAsync(AbortToken))
            .Should()
            .Be(0, "the aborted root's real transaction must roll back the earlier row");

        var act = () => root.UnitOfWork.CompleteAsync(AbortToken).AsTask();

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*A nested unit of work was disposed without completing*");

        await root.DisposeAsync(); // Releases the connection; the unit already reached its terminal state.
    }

    [Fact]
    public virtual async Task should_refuse_root_complete_while_a_child_of_the_same_resource_is_active()
    {
        await using var session = fixture.CreateSession();
        await using var root = await fixture.BeginOwnedAsync(session.Manager, AbortToken);
        var resource = root.UnitOfWork.Resource ?? throw new InvalidOperationException("The root exposed no resource.");
        await using var child = await session.Manager.BeginAsync(
            _ => ValueTask.FromResult(resource),
            options: null,
            cancellationToken: AbortToken
        );

        var act = () => root.UnitOfWork.CompleteAsync(AbortToken).AsTask();

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*A nested unit of work begun in this scope is still active*");

        root.UnitOfWork.State.Should().Be(UnitOfWorkState.Active, "a refused complete claims no outcome");

        await child.CompleteAsync(AbortToken);
        await root.UnitOfWork.CompleteAsync(AbortToken);

        root.UnitOfWork.State.Should().Be(UnitOfWorkState.Completed);
    }

    [Fact]
    public virtual async Task should_throw_and_roll_back_the_rejected_transaction_when_a_second_resource_is_begun_under_an_active_owned_unit()
    {
        await fixture.ResetAsync(AbortToken);
        await using var session = fixture.CreateSession();
        await using var first = await fixture.BeginOwnedAsync(session.Manager, AbortToken);

        await fixture.InsertProbeRowAsync(first.UnitOfWork, "first-row", AbortToken);

        // A second owned begin opens its own connection and transaction before the manager rejects it: the
        // rejection must roll that second (never-adopted) transaction back rather than leaking it.
        var act = () => fixture.BeginOwnedAsync(session.Manager, AbortToken).AsTask();

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*already active on another resource*IServiceScopeFactory.CreateScope()*");

        session
            .Manager.Current.Should()
            .BeSameAs(first.UnitOfWork, "the rejected begin leaves the active unit untouched");

        await first.UnitOfWork.CompleteAsync(AbortToken);

        (await fixture.CountProbeRowsAsync(AbortToken))
            .Should()
            .Be(1, "only the first (accepted) unit's row is durable");
    }

    [Fact]
    public virtual async Task should_roll_back_the_probe_row_and_warn_when_the_scope_disposes_with_an_active_owned_unit()
    {
        await fixture.ResetAsync(AbortToken);
        using var logs = new CapturingLoggerProvider();
        var session = fixture.CreateSession(logs);
        var handle = await fixture.BeginOwnedAsync(session.Manager, AbortToken);
        UnitOfWorkFailure? failure = null;
        handle.UnitOfWork.OnFailed(f =>
        {
            failure = f;

            return ValueTask.CompletedTask;
        });

        await fixture.InsertProbeRowAsync(handle.UnitOfWork, "scope-leak", AbortToken);

        // Disposing the scope (never the unit itself) is the leak this scenario proves recovery from.
        await session.DisposeAsync();

        failure.Should().NotBeNull();
        failure!.Reason.Should().Be(UnitOfWorkFailureReason.ScopeDisposed);
        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0, "the leaked unit's transaction must roll back");

        var warning = logs.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain("still active when its service scope was disposed");

        await handle.DisposeAsync(); // Releases the connection; the unit already reached its terminal state.
    }

    [Fact]
    public virtual async Task should_drain_without_committing_when_an_observed_unit_completes_after_the_callers_commit()
    {
        await fixture.ResetAsync(AbortToken);
        using var logs = new CapturingLoggerProvider();
        await using var session = fixture.CreateSession(logs);
        await using var handle = await fixture.EnlistObservedAsync(session.Manager, AbortToken);
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

        await using (var handle = await fixture.EnlistObservedAsync(session.Manager, AbortToken))
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
        await using var handle = await fixture.EnlistObservedAsync(session.Manager, AbortToken);
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
