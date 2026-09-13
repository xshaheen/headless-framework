// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Testing.Tests;

namespace Tests;

public sealed class CommitScopeFactoryTests : TestBase
{
    [Fact]
    public async Task should_reject_unspecified_outcome_before_claiming_scope_signal()
    {
        var factory = new CommitScopeFactory(new CommitScopeStack());
        var calls = 0;

        await using var scope = factory.Open(relational: null);
        scope.Coordinator.OnCommit(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        var act = () => scope.SignalAsync(CommitOutcome.Unspecified).AsTask();

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("outcome");
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Active);
        calls.Should().Be(0);

        await scope.SignalAsync(CommitOutcome.Committed);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task should_set_ambient_current_synchronously_and_clear_it_on_dispose()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);

        var scope = factory.Open(relational: null);
        stack.Current.Should().BeSameAs(scope.Coordinator, "the push happens in the opening frame");

        await scope.DisposeAsync();

        stack.Current.Should().BeNull();
    }

    [Fact]
    public async Task should_open_independent_root_with_its_own_relational_handle_when_ambient_scope_active()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var outerRelational = new StubRelationalCommitContext();
        var innerRelational = new StubRelationalCommitContext();

        await using var outer = factory.Open(outerRelational);
        await using var inner = factory.Open(innerRelational);

        inner.Coordinator.Should().NotBeSameAs(outer.Coordinator);
        inner.Coordinator.Relational.Should().BeSameAs(innerRelational);
        outer.Coordinator.Relational.Should().BeSameAs(outerRelational);
        stack.Current.Should().BeSameAs(inner.Coordinator);
    }

    [Fact]
    public async Task should_restore_outer_frame_after_nested_root_is_disposed()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);

        await using var outer = factory.Open(relational: null);

        await using (factory.Open(relational: null))
        {
            stack.Current.Should().NotBeSameAs(outer.Coordinator);
        }

        stack.Current.Should().BeSameAs(outer.Coordinator);
    }

    [Fact]
    public async Task should_discard_work_and_dispose_state_when_scope_is_disposed_without_signal()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var calls = 0;
        DisposableState state;

        await using (var scope = factory.Open(relational: null))
        {
            state = scope.Coordinator.GetOrAdd(static _ => new DisposableState());
            scope.Coordinator.OnCommit(() =>
            {
                calls++;

                return ValueTask.CompletedTask;
            });
        }

        calls.Should().Be(0);
        state.IsDisposed.Should().BeTrue("the async dispose awaits the rollback drain inline");
        stack.Current.Should().BeNull();
    }

    [Fact]
    public async Task should_dispose_state_in_background_when_sync_dispose_is_unsignalled()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ICommitCoordinator coordinator;

        using (var scope = factory.Open(relational: null))
        {
            coordinator = scope.Coordinator;
            scope.Coordinator.GetOrAdd(_ => new SignallingState(disposed));
        }

        // The pop and the claim are synchronous in the disposing frame; only the state disposal is offloaded.
        stack.Current.Should().BeNull();
        coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);

        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
    }

    [Fact]
    public async Task should_not_deadlock_when_sync_dispose_disposes_async_state_under_a_synchronization_context()
    {
        var factory = new CommitScopeFactory(new CommitScopeStack());
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var completed = SingleThreadSynchronizationContext.Run(
            () =>
            {
                using var scope = factory.Open(relational: null);

                // Posts its continuation back to the captured SynchronizationContext; a sync-over-async disposal on
                // the disposing thread would deadlock here unless the drain is offloaded.
                scope.Coordinator.GetOrAdd(_ => new YieldingAsyncState(disposed));
            },
            TimeSpan.FromSeconds(10)
        );

        completed
            .Should()
            .BeTrue("sync Dispose must offload the abandon drain off the captured SynchronizationContext");
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public async Task should_not_roll_back_committed_work_when_disposed_before_the_commit_drain_completes()
    {
        var factory = new CommitScopeFactory(new CommitScopeStack());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committed = false;

        var scope = factory.Open(relational: null);
        var state = scope.Coordinator.GetOrAdd(static _ => new DisposableState());
        scope.Coordinator.OnCommit(async () =>
        {
            await gate.Task;
            committed = true;
        });

        // Claim the commit and start the drain; it blocks on the gate, so the drain is still in flight.
        var drain = scope.SignalAsync(CommitOutcome.Committed);

        // Dispose while the commit drain is pending. The terminal outcome was claimed synchronously by the signal,
        // so disposal must observe it and neither re-claim nor dispose the state out from under the drain.
        await scope.DisposeAsync();

        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
        committed.Should().BeFalse("the drain is still gated");
        state.IsDisposed.Should().BeFalse("the in-flight commit drain owns the scope state");

        gate.SetResult();
        await drain;

        committed.Should().BeTrue();
        state.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task should_pop_ambient_once_and_not_re_signal_when_disposed_after_signaling()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var commits = 0;

        var scope = factory.Open(relational: null);
        scope.Coordinator.OnCommit(() =>
        {
            Interlocked.Increment(ref commits);

            return ValueTask.CompletedTask;
        });

        await scope.SignalAsync(CommitOutcome.Committed);
        stack.Current.Should().NotBeNull("the ambient frame is owned by disposal, not by the signal");

        await scope.DisposeAsync();
        await scope.DisposeAsync();

        commits.Should().Be(1, "disposal after a signal must not drain a second terminal outcome");
        stack.Current.Should().BeNull();
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public async Task should_ignore_signal_after_dispose()
    {
        var factory = new CommitScopeFactory(new CommitScopeStack());
        var calls = 0;

        var scope = factory.Open(relational: null);
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

    [Fact]
    public async Task should_observe_exactly_one_outcome_when_signal_and_dispose_race()
    {
        // Each iteration runs in its own flow so the ambient frames it pushes never leak into the test's flow.
        for (var iteration = 0; iteration < 200; iteration++)
        {
            await Task.Run(
                async () =>
                {
                    var factory = new CommitScopeFactory(new CommitScopeStack());
                    var scope = factory.Open(relational: null);
                    var calls = 0;
                    var disposals = 0;
                    scope.Coordinator.GetOrAdd(_ => new CountingState(() => Interlocked.Increment(ref disposals)));
                    scope.Coordinator.OnCommit(() =>
                    {
                        Interlocked.Increment(ref calls);

                        return ValueTask.CompletedTask;
                    });
                    using var start = new Barrier(2);

                    var signal = Task.Run(
                        async () =>
                        {
                            start.SignalAndWait();
                            await scope.SignalAsync(CommitOutcome.Committed);
                        },
                        AbortToken
                    );
                    var dispose = Task.Run(
                        async () =>
                        {
                            start.SignalAndWait();
                            await scope.DisposeAsync();
                        },
                        AbortToken
                    );

                    await Task.WhenAll(signal, dispose);

                    var outcome = scope.Coordinator.State;
                    outcome.Should().BeOneOf(CommitCoordinatorState.Committed, CommitCoordinatorState.RolledBack);
                    calls
                        .Should()
                        .Be(
                            outcome == CommitCoordinatorState.Committed ? 1 : 0,
                            "work is drained or discarded, never both"
                        );
                    disposals.Should().Be(1, "scope state is disposed exactly once whichever side wins");
                },
                AbortToken
            );
        }
    }

    [Fact]
    public async Task should_throw_when_outer_scope_is_disposed_while_inner_is_active()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);

        var outer = factory.Open(relational: null);
        var inner = factory.Open(relational: null);

        var act = () => outer.DisposeAsync().AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Commit scope disposed out of order.");
        outer.Coordinator.State.Should().Be(CommitCoordinatorState.Active, "a rejected dispose claims no outcome");
        stack.Current.Should().BeSameAs(inner.Coordinator);

        // Unwind in order so the ambient frame does not leak into the async flow.
        await inner.DisposeAsync();
        await outer.DisposeAsync();

        stack.Current.Should().BeNull();
        outer.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
    }

    [Fact]
    public void should_throw_when_pop_handle_outer_frame_disposed_before_inner()
    {
        var stack = new CommitScopeStack();

        var outer = stack.Push(new CommitCoordinator());
        var inner = stack.Push(new CommitCoordinator());

        var act = outer.Dispose;

        act.Should().Throw<InvalidOperationException>().WithMessage("Commit scope disposed out of order.");

        // Unwind in order so the ambient frame does not leak into the async flow.
        inner.Dispose();
        outer.Dispose();
    }

    [Fact]
    public async Task should_ignore_pop_of_frame_its_parent_already_popped()
    {
        var stack = new CommitScopeStack();
        var outerCoordinator = new CommitCoordinator();
        var outer = stack.Push(outerCoordinator);
        IDisposable inner = null!;

        async Task pushInIsolatedFlowAsync()
        {
            inner = stack.Push(new CommitCoordinator());
            await Task.Yield();
        }

        // The async method's execution context is restored on return, so the inner frame exists only in that
        // flow; from here the outer frame is current and the inner frame counts as already popped.
        await pushInIsolatedFlowAsync();
        stack.Current.Should().BeSameAs(outerCoordinator);

        var act = inner.Dispose;

        act.Should().NotThrow();
        stack.Current.Should().BeSameAs(outerCoordinator);

        outer.Dispose();
        stack.Current.Should().BeNull();
    }

    [Fact]
    public async Task should_get_or_add_same_instance_and_dispose_it_on_both_outcomes()
    {
        var factory = new CommitScopeFactory(new CommitScopeStack());

        await using var committed = factory.Open(relational: null);
        var committedState = committed.Coordinator.GetOrAdd(static _ => new DisposableState());
        committed.Coordinator.GetOrAdd(static _ => new DisposableState()).Should().BeSameAs(committedState);
        await committed.SignalAsync(CommitOutcome.Committed);
        committedState.IsDisposed.Should().BeTrue();

        await using var rolledBack = factory.Open(relational: null);
        var rolledBackState = rolledBack.Coordinator.GetOrAdd(static _ => new DisposableState());
        rolledBackState.Should().NotBeSameAs(committedState, "state is scoped to one coordinator");
        await rolledBack.SignalAsync(CommitOutcome.RolledBack);
        rolledBackState.IsDisposed.Should().BeTrue();
    }

    private sealed class DisposableState : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class CountingState(Action onDispose) : IDisposable
    {
        public void Dispose()
        {
            onDispose();
        }
    }

    private sealed class SignallingState(TaskCompletionSource disposed) : IDisposable
    {
        public void Dispose()
        {
            disposed.TrySetResult();
        }
    }

    private sealed class YieldingAsyncState(TaskCompletionSource disposed) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            disposed.TrySetResult();
        }
    }
}
