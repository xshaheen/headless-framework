// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Provider-agnostic specification of the unit-of-work contract: a singleton factory, explicit begin /
/// complete / rollback, completion and failure callbacks, unit-local state, the terminal-claim rules, and
/// independent units with nothing ambient. Every scenario is portable; provider-specific concerns (real transactions, EF
/// execution strategies) live in the provider projects.
/// </summary>
public abstract class UnitOfWorkConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : IUnitOfWorkFixture
{
    [Fact]
    public virtual async Task should_run_completed_work_once_after_complete()
    {
        var session = fixture.CreateSession();
        await using var unitOfWork = await session.BeginAsync(AbortToken);
        var calls = 0;

        unitOfWork.OnCompleted(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await unitOfWork.CompleteAsync(AbortToken);

        calls.Should().Be(1);
        unitOfWork.State.Should().Be(UnitOfWorkState.Completed);
    }

    [Fact]
    public virtual async Task should_throw_already_completed_when_complete_is_called_twice()
    {
        var session = fixture.CreateSession();
        await using var unitOfWork = await session.BeginAsync(AbortToken);

        await unitOfWork.CompleteAsync(AbortToken);
        var act = () => unitOfWork.CompleteAsync(AbortToken).AsTask();

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Begin a new unit of work for further work.*");
    }

    [Fact]
    public virtual async Task should_discard_completed_work_after_rollback()
    {
        var session = fixture.CreateSession();
        await using var unitOfWork = await session.BeginAsync(AbortToken);
        var calls = 0;

        unitOfWork.OnCompleted(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await unitOfWork.RollbackAsync();

        calls.Should().Be(0);
        unitOfWork.State.Should().Be(UnitOfWorkState.Failed);
        unitOfWork.Failure.Should().NotBeNull();
        unitOfWork.Failure!.Reason.Should().Be(UnitOfWorkFailureReason.RolledBack);
    }

    [Fact]
    public virtual async Task should_reject_registration_after_terminal_state()
    {
        var session = fixture.CreateSession();
        await using var unitOfWork = await session.BeginAsync(AbortToken);

        await unitOfWork.CompleteAsync(AbortToken);

        unitOfWork
            .Invoking(x => x.OnCompleted(() => ValueTask.CompletedTask))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*registrations are accepted only while it is Active.*");
        unitOfWork
            .Invoking(x => x.OnFailed(static _ => ValueTask.CompletedTask))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*registrations are accepted only while it is Active.*");
        unitOfWork
            .Invoking(x => x.GetOrAdd(static _ => new ScopeState()))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*registrations are accepted only while it is Active.*");
    }

    [Fact]
    public virtual async Task should_run_remaining_callbacks_in_order_and_surface_fault_after_drain()
    {
        var session = fixture.CreateSession();
        await using var unitOfWork = await session.BeginAsync(AbortToken);
        var order = new List<int>();

        unitOfWork.OnCompleted(() =>
        {
            order.Add(1);

            throw new InvalidOperationException("boom");
        });
        unitOfWork.OnCompleted(() =>
        {
            order.Add(2);

            return ValueTask.CompletedTask;
        });
        unitOfWork.OnCompleted(() =>
        {
            order.Add(3);

            return ValueTask.CompletedTask;
        });

        var act = () => unitOfWork.CompleteAsync(AbortToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        order.Should().Equal(1, 2, 3);
        unitOfWork
            .State.Should()
            .Be(UnitOfWorkState.Completed, "an OnCompleted fault after a successful drain is not a rollback");
    }

    [Fact]
    public virtual async Task should_run_failed_work_and_dispose_state_when_disposed_without_complete()
    {
        var session = fixture.CreateSession();
        IUnitOfWork unitOfWork = await session.BeginAsync(AbortToken);
        var completedCalls = 0;
        UnitOfWorkFailure? failure = null;

        unitOfWork.OnCompleted(() =>
        {
            completedCalls++;

            return ValueTask.CompletedTask;
        });
        unitOfWork.OnFailed(f =>
        {
            failure = f;

            return ValueTask.CompletedTask;
        });

        await unitOfWork.DisposeAsync();

        completedCalls.Should().Be(0);
        unitOfWork.State.Should().Be(UnitOfWorkState.Failed);
        failure.Should().NotBeNull();
        failure!.Reason.Should().Be(UnitOfWorkFailureReason.Abandoned);
    }

    [Fact]
    public virtual async Task should_dispose_scope_local_state_on_complete()
    {
        var session = fixture.CreateSession();
        await using var unitOfWork = await session.BeginAsync(AbortToken);

        var state = unitOfWork.GetOrAdd(static _ => new ScopeState());
        unitOfWork.GetOrAdd(static _ => new ScopeState()).Should().BeSameAs(state);

        await unitOfWork.CompleteAsync(AbortToken);

        state.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public virtual async Task should_dispose_scope_local_state_on_rollback()
    {
        var session = fixture.CreateSession();
        await using var unitOfWork = await session.BeginAsync(AbortToken);

        var state = unitOfWork.GetOrAdd(static _ => new ScopeState());

        await unitOfWork.RollbackAsync();

        state.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public virtual async Task should_treat_rollback_after_complete_as_no_op()
    {
        var session = fixture.CreateSession();
        await using var unitOfWork = await session.BeginAsync(AbortToken);
        var failedCalls = 0;

        unitOfWork.OnFailed(_ =>
        {
            failedCalls++;

            return ValueTask.CompletedTask;
        });

        await unitOfWork.CompleteAsync(AbortToken);
        await unitOfWork.RollbackAsync();

        failedCalls.Should().Be(0, "a terminal unit ignores the conflicting verb");
        unitOfWork.State.Should().Be(UnitOfWorkState.Completed);
    }

    [Fact]
    public virtual async Task should_be_idempotent_when_rollback_is_called_twice()
    {
        var session = fixture.CreateSession();
        await using var unitOfWork = await session.BeginAsync(AbortToken);
        var failedCalls = 0;

        unitOfWork.OnFailed(_ =>
        {
            failedCalls++;

            return ValueTask.CompletedTask;
        });

        await unitOfWork.RollbackAsync();
        await unitOfWork.RollbackAsync();

        failedCalls.Should().Be(1);
        unitOfWork.State.Should().Be(UnitOfWorkState.Failed);
    }

    [Fact]
    public virtual async Task should_ignore_conflicting_terminal_verb_and_stay_completed()
    {
        var session = fixture.CreateSession();
        await using var unitOfWork = await session.BeginAsync(AbortToken);
        var failedCalls = 0;

        unitOfWork.OnFailed(_ =>
        {
            failedCalls++;

            return ValueTask.CompletedTask;
        });

        await unitOfWork.CompleteAsync(AbortToken);

        // RollbackAsync after CompleteAsync is the conflicting verb: ignored, the OnFailed registered while the
        // unit was active never runs, and the unit stays Completed.
        await unitOfWork.RollbackAsync();

        failedCalls.Should().Be(0);
        unitOfWork.State.Should().Be(UnitOfWorkState.Completed);
    }

    [Fact]
    public virtual async Task should_throw_object_disposed_when_any_member_runs_after_dispose()
    {
        var session = fixture.CreateSession();
        var unitOfWork = await session.BeginAsync(AbortToken);
        await unitOfWork.DisposeAsync();

        var act = () => unitOfWork.CompleteAsync(AbortToken).AsTask();

        await act.Should().ThrowAsync<ObjectDisposedException>();
        unitOfWork
            .Invoking(x => x.OnCompleted(() => ValueTask.CompletedTask))
            .Should()
            .Throw<ObjectDisposedException>();
        await unitOfWork.Invoking(x => x.RollbackAsync().AsTask()).Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public virtual async Task should_not_roll_back_committed_work_when_dispose_races_the_completion_drain()
    {
        var session = fixture.CreateSession();
        var unitOfWork = await session.BeginAsync(AbortToken);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;
        var state = unitOfWork.GetOrAdd(static _ => new ScopeState());

        unitOfWork.OnCompleted(async () =>
        {
            await gate.Task;
            completed = true;
        });

        // The terminal claim settles synchronously in CompleteAsync; the drain is parked on the gate.
        var drain = unitOfWork.CompleteAsync(AbortToken);

        await unitOfWork.DisposeAsync();

        unitOfWork.State.Should().Be(UnitOfWorkState.Completed);
        completed.Should().BeFalse("the drain is still gated");
        state
            .IsDisposed.Should()
            .BeFalse("scope-local state belongs to the in-flight completion drain, not the dispose");

        gate.SetResult();
        await drain;

        completed.Should().BeTrue();
        state.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public virtual async Task should_expose_null_resource_for_resource_less_unit()
    {
        var session = fixture.CreateSession();
        await using var unitOfWork = await session.BeginAsync(AbortToken);

        unitOfWork.Resource.Should().BeNull();
    }

    [Fact]
    public virtual async Task should_expose_the_relational_handle_for_a_resource_bearing_unit()
    {
        var session = fixture.CreateSession();
        var resource = new StubRelationalUnitOfWorkResource();

        await using var unitOfWork = await session.Factory.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
            options: null,
            cancellationToken: AbortToken
        );

        unitOfWork.Resource.Should().BeSameAs(resource);
        unitOfWork.Resource.Should().BeAssignableTo<IRelationalUnitOfWorkResource>();
    }

    // Factory scenarios: units are independent, nothing is ambient, and a begin never joins another.

    [Fact]
    public virtual async Task should_open_independent_units_for_consecutive_begins()
    {
        var session = fixture.CreateSession();
        var outerCalls = 0;
        var innerCalls = 0;

        await using var outer = await session.BeginAsync(AbortToken);
        outer.OnCompleted(() =>
        {
            outerCalls++;

            return ValueTask.CompletedTask;
        });

        await using (
            var inner = await session.Factory.BeginAsync(
                _ => ValueTask.FromResult<IUnitOfWorkResource>(new StubRelationalUnitOfWorkResource()),
                options: null,
                cancellationToken: AbortToken
            )
        )
        {
            inner.Should().NotBeSameAs(outer, "every begin opens its own unit; there is nothing to join");
            inner.OnCompleted(() =>
            {
                innerCalls++;

                return ValueTask.CompletedTask;
            });

            await inner.CompleteAsync(AbortToken);
        }

        innerCalls.Should().Be(1);
        outerCalls.Should().Be(0, "the inner unit's outcome is its own");
        outer.State.Should().Be(UnitOfWorkState.Active);

        await outer.RollbackAsync();

        outerCalls.Should().Be(0);
        innerCalls.Should().Be(1);
    }

    [Fact]
    public virtual async Task should_open_two_units_on_the_same_resource_without_joining()
    {
        // Join-by-default went with the ambient slot: a callee that must share a unit is handed it. Two begins on
        // one resource are therefore two units, each with its own registrations and its own completion.
        var session = fixture.CreateSession();
        var resource = new FakeUnitOfWorkResource();
        var order = new List<string>();

        await using var first = await session.Factory.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
            options: null,
            cancellationToken: AbortToken
        );
        await using var second = await session.Factory.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
            options: null,
            cancellationToken: AbortToken
        );

        second.Should().NotBeSameAs(first);
        first.OnCompleted(() =>
        {
            order.Add("first");

            return ValueTask.CompletedTask;
        });
        second.OnCompleted(() =>
        {
            order.Add("second");

            return ValueTask.CompletedTask;
        });

        await second.CompleteAsync(AbortToken);

        order.Should().Equal("second");
        first.State.Should().Be(UnitOfWorkState.Active, "the other unit is untouched");

        await first.CompleteAsync(AbortToken);

        order.Should().Equal("second", "first");
    }

    [Fact]
    public virtual async Task should_propagate_a_resource_begin_fault_and_stay_usable()
    {
        var session = fixture.CreateSession();

        var act = () =>
            session
                .Factory.BeginAsync(
                    static _ =>
                        ValueTask.FromException<IUnitOfWorkResource>(new InvalidOperationException("begin fault")),
                    options: null,
                    cancellationToken: AbortToken
                )
                .AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("begin fault");

        await using var second = await session.Factory.BeginAsync(
            static _ => ValueTask.FromResult<IUnitOfWorkResource>(new FakeUnitOfWorkResource()),
            options: null,
            cancellationToken: AbortToken
        );

        second.State.Should().Be(UnitOfWorkState.Active, "a faulted begin leaves nothing behind");
    }

    [Fact]
    public virtual async Task should_commit_the_owned_resource_on_complete()
    {
        var session = fixture.CreateSession();
        var resource = new FakeUnitOfWorkResource();

        await using var unitOfWork = await session.Factory.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
            options: null,
            cancellationToken: AbortToken
        );

        await unitOfWork.CompleteAsync(AbortToken);

        resource.CommitCalls.Should().Be(1);
        resource.RollbackCalls.Should().Be(0);
    }

    [Fact]
    public virtual async Task should_roll_back_the_owned_resource_on_rollback_and_abandon()
    {
        var session = fixture.CreateSession();
        var resource = new FakeUnitOfWorkResource();
        var unitOfWork = await session.Factory.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
            options: null,
            cancellationToken: AbortToken
        );

        await unitOfWork.RollbackAsync();

        resource.RollbackCalls.Should().Be(1);

        var second = new FakeUnitOfWorkResource();
        var secondUnit = await session.Factory.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(second),
            options: null,
            cancellationToken: AbortToken
        );

        await secondUnit.DisposeAsync();

        second.RollbackCalls.Should().Be(1, "dispose without complete rolls the owned resource back");
    }

    [Fact]
    public virtual async Task should_not_commit_an_observed_resource()
    {
        var session = fixture.CreateSession();
        var resource = new FakeUnitOfWorkResource(isOwned: false);

        await using var unitOfWork = await session.Factory.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
            options: null,
            cancellationToken: AbortToken
        );

        await unitOfWork.CompleteAsync(AbortToken);

        resource.CommitCalls.Should().Be(0, "observed mode never commits the resource");
    }

    [Fact]
    public virtual async Task should_log_the_forgotten_completion_warning_for_an_observed_unit_disposed_after_a_finished_transaction()
    {
        var session = fixture.CreateSession();
        var resource = new FakeUnitOfWorkResource(isOwned: false) { TransactionCompleted = true };

        var unitOfWork = await session.Factory.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
            options: null,
            cancellationToken: AbortToken
        );

        await unitOfWork.DisposeAsync();

        var warning = session.Logs.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain("was disposed without CompleteAsync or RollbackAsync");
        warning.Message.Should().Contain("durable rows will be recovered by the relay");
    }

    [Fact]
    public virtual async Task should_not_log_the_forgotten_completion_warning_after_an_explicit_rollback()
    {
        var session = fixture.CreateSession();
        var resource = new FakeUnitOfWorkResource(isOwned: false) { TransactionCompleted = true };

        var unitOfWork = await session.Factory.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
            options: null,
            cancellationToken: AbortToken
        );

        await unitOfWork.RollbackAsync();
        await unitOfWork.DisposeAsync();

        session.Logs.Should().BeEmpty("RollbackAsync suppresses the forgotten-completion warning");
    }

    [Fact]
    public virtual async Task should_run_on_failed_with_faulted_reason_when_the_commit_faults()
    {
        var session = fixture.CreateSession();
        var resource = new FakeUnitOfWorkResource { CommitFault = new InvalidOperationException("commit fault") };

        await using var unitOfWork = await session.Factory.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
            options: null,
            cancellationToken: AbortToken
        );
        UnitOfWorkFailure? failure = null;
        unitOfWork.OnFailed(f =>
        {
            failure = f;

            return ValueTask.CompletedTask;
        });

        var act = () => unitOfWork.CompleteAsync(AbortToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("commit fault");
        unitOfWork
            .State.Should()
            .Be(UnitOfWorkState.Failed, "a commit fault transitions to Failed before the exception propagates");
        failure!.Reason.Should().Be(UnitOfWorkFailureReason.Faulted);
        failure.Exception.Should().BeOfType<InvalidOperationException>();
        resource
            .RollbackCalls.Should()
            .Be(1, "a commit that never reached the database leaves the transaction open; the unit rolls it back");
        session.Logs.Should().BeEmpty("a successful rollback after the commit fault is not an incident");

        var second = () => unitOfWork.CompleteAsync(AbortToken).AsTask();

        await second
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*already failed (Faulted)*Begin a new unit of work.*");
    }

    [Fact]
    public virtual async Task should_log_and_still_surface_the_commit_fault_when_the_rollback_after_it_faults_too()
    {
        var session = fixture.CreateSession();
        var resource = new FakeUnitOfWorkResource
        {
            CommitFault = new InvalidOperationException("commit fault"),
            RollbackFault = new InvalidOperationException("rollback fault"),
        };

        await using var unitOfWork = await session.Factory.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
            options: null,
            cancellationToken: AbortToken
        );

        var act = () => unitOfWork.CompleteAsync(AbortToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("commit fault");
        unitOfWork.State.Should().Be(UnitOfWorkState.Failed);
        resource.RollbackCalls.Should().Be(1);

        var entry = session.Logs.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Contain("whose commit faulted failed as well");
    }

    [Fact]
    public virtual async Task should_leave_state_completed_when_an_on_completed_callback_faults()
    {
        var session = fixture.CreateSession();
        await using var unitOfWork = await session.BeginAsync(AbortToken);

        unitOfWork.OnCompleted(() => throw new InvalidOperationException("callback fault"));

        var act = () => unitOfWork.CompleteAsync(AbortToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("callback fault");
        unitOfWork.State.Should().Be(UnitOfWorkState.Completed);
    }

    [Fact]
    public virtual async Task should_support_prevent_retry_and_report_it()
    {
        var session = fixture.CreateSession();
        await using var unitOfWork = await session.BeginAsync(AbortToken);

        unitOfWork.IsRetryPrevented.Should().BeFalse();
        unitOfWork.PreventRetry();
        unitOfWork.IsRetryPrevented.Should().BeTrue();
    }

    private sealed class ScopeState : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
