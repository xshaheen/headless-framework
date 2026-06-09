// Copyright (c) Mahmoud Shaheen. All rights reserved.

using AwesomeAssertions;
using Headless.CommitCoordination;

namespace Tests;

public sealed class CommitScopeFactoryTests
{
    [Fact]
    public async Task should_restore_parent_current_after_child_scope_disposes()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var services = new EmptyServiceProvider();

        await using var parent = factory.Begin(services);
        var parentCoordinator = stack.Current;

        await using (factory.Begin(services))
        {
            stack.Current.Should().NotBeSameAs(parentCoordinator);
        }

        stack.Current.Should().BeSameAs(parentCoordinator);
    }

    [Fact]
    public async Task should_promote_child_commit_work_to_parent_root()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var services = new EmptyServiceProvider();
        var calls = 0;

        await using var parent = factory.Begin(services);

        await using (var child = factory.Begin(services))
        {
            child.Coordinator.OnCommit((_, _) =>
            {
                calls++;

                return ValueTask.CompletedTask;
            });

            await child.SignalAsync(CommitOutcome.Committed, CancellationToken.None);
        }

        calls.Should().Be(0);

        await parent.SignalAsync(CommitOutcome.Committed, CancellationToken.None);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task should_doom_parent_when_child_rolls_back()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var services = new EmptyServiceProvider();
        var calls = 0;

        await using var parent = factory.Begin(services);
        parent.Coordinator.OnCommit((_, _) =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await using (var child = factory.Begin(services))
        {
            await child.SignalAsync(CommitOutcome.RolledBack, CancellationToken.None);
        }

        parent.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task should_discard_work_when_scope_is_disposed_without_signal()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var services = new EmptyServiceProvider();
        var calls = 0;

        await using (var scope = factory.Begin(services))
        {
            scope.Coordinator.OnCommit((_, _) =>
            {
                calls++;

                return ValueTask.CompletedTask;
            });
        }

        calls.Should().Be(0);
    }

    [Fact]
    public void should_run_rollback_callbacks_when_sync_scope_is_disposed_without_signal()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var services = new EmptyServiceProvider();
        var calls = 0;

        using (var scope = factory.Begin(services))
        {
            scope.Coordinator.OnRollback((_, _) =>
            {
                calls++;

                return ValueTask.CompletedTask;
            });
        }

        calls.Should().Be(1);
        stack.Current.Should().BeNull();
    }

    [Fact]
    public void should_not_deadlock_when_sync_dispose_drains_rollback_under_a_synchronization_context()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var services = new EmptyServiceProvider();
        var ran = false;

        var completed = SingleThreadSynchronizationContext.Run(
            () =>
            {
                using var scope = factory.Begin(services);

                scope.Coordinator.OnRollback(async (_, _) =>
                {
                    // Posts the continuation back to the captured SynchronizationContext; a sync-over-async drain
                    // on the disposing thread would deadlock here unless the drain is offloaded.
                    await Task.Yield();
                    ran = true;
                });
            },
            TimeSpan.FromSeconds(10)
        );

        completed.Should().BeTrue("sync Dispose must offload the rollback drain off the captured SynchronizationContext");
        ran.Should().BeTrue();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>
    /// Runs an action on a dedicated thread carrying a single-threaded <see cref="SynchronizationContext" /> whose
    /// posted continuations are only pumped after the action returns. If the action blocks on an async continuation
    /// posted to this context, the thread deadlocks and <see cref="Run" /> reports a timeout.
    /// </summary>
    private sealed class SingleThreadSynchronizationContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public static bool Run(Action action, TimeSpan timeout)
        {
            using var completed = new ManualResetEventSlim(false);
            var context = new SingleThreadSynchronizationContext();

            var thread = new Thread(() =>
            {
                SetSynchronizationContext(context);

                try
                {
                    action();
                }
                finally
                {
                    completed.Set();
                    context._queue.CompleteAdding();
                }

                foreach (var (callback, state) in context._queue.GetConsumingEnumerable())
                {
                    callback(state);
                }
            })
            {
                IsBackground = true,
            };

            thread.Start();

            return completed.Wait(timeout);
        }
    }
}
