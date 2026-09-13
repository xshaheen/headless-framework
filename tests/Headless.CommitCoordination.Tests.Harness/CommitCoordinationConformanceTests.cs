// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Provider-agnostic specification of the minimal coordinator contract: one factory method that opens an
/// independent root, commit callbacks, scope-local state, explicit signaling, and the ambient frame. Every
/// scenario is portable; provider-specific concerns (synchronization-context behavior, durable rows) live
/// in the concrete runners or the coordinated-transaction harness instead of this base.
/// </summary>
public abstract class CommitCoordinationConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : ICommitCoordinationFixture
{
    public virtual async Task should_run_commit_work_once_after_commit_signal()
    {
        var session = fixture.CreateSession();
        await using var scope = session.Open();
        var calls = 0;

        scope.Coordinator.OnCommit(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await scope.SignalAsync(CommitOutcome.Committed);

        calls.Should().Be(1);
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    public virtual async Task should_treat_repeated_same_outcome_signal_as_silent_no_op()
    {
        var session = fixture.CreateSession();
        await using var scope = session.Open();
        var calls = 0;

        scope.Coordinator.OnCommit(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await scope.SignalAsync(CommitOutcome.Committed);
        await scope.SignalAsync(CommitOutcome.Committed);

        calls.Should().Be(1);
        session.Logs.Should().NotContain(e => e.Level >= LogLevel.Warning, "a repeated same-outcome signal is silent");
    }

    public virtual async Task should_discard_commit_work_after_rollback_signal()
    {
        var session = fixture.CreateSession();
        await using var scope = session.Open();
        var calls = 0;

        scope.Coordinator.OnCommit(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await scope.SignalAsync(CommitOutcome.RolledBack);

        calls.Should().Be(0);
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
    }

    public virtual async Task should_reject_enlistment_after_terminal_signal()
    {
        var session = fixture.CreateSession();
        await using var scope = session.Open();

        await scope.SignalAsync(CommitOutcome.Committed);

        scope
            .Coordinator.Invoking(x => x.OnCommit(() => ValueTask.CompletedTask))
            .Should()
            .Throw<InvalidOperationException>();
        scope
            .Coordinator.Invoking(x => x.GetOrAdd(static _ => new ScopeState()))
            .Should()
            .Throw<InvalidOperationException>();
    }

    public virtual async Task should_run_remaining_callbacks_in_order_and_surface_fault_after_drain()
    {
        var session = fixture.CreateSession();
        await using var scope = session.Open();
        var order = new List<int>();

        scope.Coordinator.OnCommit(() =>
        {
            order.Add(1);

            throw new InvalidOperationException("boom");
        });
        scope.Coordinator.OnCommit(() =>
        {
            order.Add(2);

            return ValueTask.CompletedTask;
        });
        scope.Coordinator.OnCommit(() =>
        {
            order.Add(3);

            return ValueTask.CompletedTask;
        });

        var act = () => scope.SignalAsync(CommitOutcome.Committed).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        order.Should().Equal(1, 2, 3);
    }

    public virtual async Task should_roll_back_and_discard_work_when_scope_is_disposed_without_signal()
    {
        var session = fixture.CreateSession();
        var calls = 0;
        ICommitCoordinator coordinator;

        await using (var scope = session.Open())
        {
            coordinator = scope.Coordinator;
            scope.Coordinator.OnCommit(() =>
            {
                calls++;

                return ValueTask.CompletedTask;
            });
        }

        calls.Should().Be(0);
        coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
        session.Ambient.Current.Should().BeNull();
    }

    public virtual async Task should_dispose_scope_local_state_on_commit()
    {
        var session = fixture.CreateSession();
        await using var scope = session.Open();

        var state = scope.Coordinator.GetOrAdd(static _ => new ScopeState());
        scope.Coordinator.GetOrAdd(static _ => new ScopeState()).Should().BeSameAs(state);

        await scope.SignalAsync(CommitOutcome.Committed);

        state.IsDisposed.Should().BeTrue();
    }

    public virtual async Task should_dispose_scope_local_state_on_rollback()
    {
        var session = fixture.CreateSession();
        await using var scope = session.Open();

        var state = scope.Coordinator.GetOrAdd(static _ => new ScopeState());

        await scope.SignalAsync(CommitOutcome.RolledBack);

        state.IsDisposed.Should().BeTrue();
    }

    public virtual async Task should_set_ambient_current_synchronously_when_scope_opens()
    {
        var session = fixture.CreateSession();
        session.Ambient.Current.Should().BeNull();

        // No await between Open and the read: the ambient frame must already be visible in the opening frame,
        // otherwise consumers enlisting right after the open would silently miss the coordinator.
        await using var scope = session.Open();
        session.Ambient.Current.Should().BeSameAs(scope.Coordinator);
    }

    public virtual async Task should_restore_outer_frame_when_nested_root_is_disposed_in_order()
    {
        var session = fixture.CreateSession();

        await using var outer = session.Open();
        var inner = session.Open();

        inner.Coordinator.Should().NotBeSameAs(outer.Coordinator, "every Open starts an independent root");
        session.Ambient.Current.Should().BeSameAs(inner.Coordinator);

        await inner.DisposeAsync();

        session.Ambient.Current.Should().BeSameAs(outer.Coordinator);
        outer.Coordinator.State.Should().Be(CommitCoordinatorState.Active, "the inner root's outcome is its own");
    }

    public virtual async Task should_not_promote_nested_root_work_to_outer_root()
    {
        var session = fixture.CreateSession();
        var outerCalls = 0;
        var innerCalls = 0;

        await using var outer = session.Open();
        outer.Coordinator.OnCommit(() =>
        {
            outerCalls++;

            return ValueTask.CompletedTask;
        });

        await using (var inner = session.Open())
        {
            inner.Coordinator.OnCommit(() =>
            {
                innerCalls++;

                return ValueTask.CompletedTask;
            });

            await inner.SignalAsync(CommitOutcome.Committed);
        }

        innerCalls.Should().Be(1);
        outerCalls.Should().Be(0);

        await outer.SignalAsync(CommitOutcome.RolledBack);

        outerCalls.Should().Be(0);
        innerCalls.Should().Be(1);
    }

    public virtual async Task should_throw_when_outer_scope_is_disposed_while_inner_is_active()
    {
        var session = fixture.CreateSession();

        var outer = session.Open();
        var inner = session.Open();

        var act = () => outer.DisposeAsync().AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        session.Ambient.Current.Should().BeSameAs(inner.Coordinator, "a rejected dispose leaves the frames intact");
        outer.Coordinator.State.Should().Be(CommitCoordinatorState.Active, "a rejected dispose claims no outcome");

        // Unwinding in order still works after the rejected attempt.
        await inner.DisposeAsync();
        await outer.DisposeAsync();

        session.Ambient.Current.Should().BeNull();
    }

    public virtual async Task should_ignore_disposal_of_frame_its_parent_already_popped()
    {
        var session = fixture.CreateSession();
        await using var outer = session.Open();
        ICommitScope inner = null!;

        async Task openInIsolatedFlowAsync()
        {
            inner = session.Open();
            await Task.Yield();
            session.Ambient.Current.Should().BeSameAs(inner.Coordinator);
        }

        // The async method's execution context is restored on return, so the inner frame was pushed only in
        // that flow: from here the outer frame is current and the inner frame counts as already popped.
        await openInIsolatedFlowAsync();
        session.Ambient.Current.Should().BeSameAs(outer.Coordinator);

        await inner.DisposeAsync();

        session.Ambient.Current.Should().BeSameAs(outer.Coordinator, "popping an already-popped frame is a no-op");
        inner.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack, "the un-signalled dispose still claims");
    }

    public virtual async Task should_ignore_signal_after_dispose()
    {
        var session = fixture.CreateSession();
        var scope = session.Open();
        var calls = 0;

        scope.Coordinator.OnCommit(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await scope.DisposeAsync();
        await scope.SignalAsync(CommitOutcome.Committed);

        calls.Should().Be(0);
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
    }

    public virtual async Task should_ignore_and_log_conflicting_second_signal()
    {
        var session = fixture.CreateSession();
        await using var scope = session.Open();
        var calls = 0;

        scope.Coordinator.OnCommit(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await scope.SignalAsync(CommitOutcome.Committed);
        await scope.SignalAsync(CommitOutcome.RolledBack);

        calls.Should().Be(1);
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);

        var warning = session.Logs.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain(nameof(CommitOutcome.RolledBack), "the ignored signal is named");
        warning.Message.Should().Contain(nameof(CommitCoordinatorState.Committed), "the winning state is named");
    }

    public virtual async Task should_not_roll_back_committed_work_when_dispose_races_the_commit_drain()
    {
        var session = fixture.CreateSession();
        var scope = session.Open();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committed = false;
        var state = scope.Coordinator.GetOrAdd(static _ => new ScopeState());

        scope.Coordinator.OnCommit(async () =>
        {
            await gate.Task;
            committed = true;
        });

        // The claim settles synchronously in SignalAsync; the drain is parked on the gate.
        var drain = scope.SignalAsync(CommitOutcome.Committed);

        await scope.DisposeAsync();

        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
        committed.Should().BeFalse("the drain is still gated");
        state.IsDisposed.Should().BeFalse("scope-local state belongs to the in-flight commit drain, not the dispose");

        gate.SetResult();
        await drain;

        committed.Should().BeTrue();
        state.IsDisposed.Should().BeTrue();
    }

    public virtual async Task should_expose_null_relational_for_non_relational_scope()
    {
        var session = fixture.CreateSession();
        await using var scope = session.Open();

        scope.Coordinator.Relational.Should().BeNull();
    }

    public virtual async Task should_expose_relational_handle_for_relational_scope()
    {
        var session = fixture.CreateSession();
        var relational = new StubRelationalCommitContext();
        await using var scope = session.Open(relational);

        scope.Coordinator.Relational.Should().BeSameAs(relational);
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
