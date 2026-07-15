// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.CommitCoordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class CommitScopeFactoryTests : TestBase
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
            child.Coordinator.OnCommit(
                (_, _) =>
                {
                    calls++;

                    return ValueTask.CompletedTask;
                }
            );

            await child.SignalAsync(CommitOutcome.Committed);
        }

        calls.Should().Be(0);

        await parent.SignalAsync(CommitOutcome.Committed);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task should_throw_when_child_enlists_after_child_commit_signal()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var services = new EmptyServiceProvider();

        await using var parent = factory.Begin(services);
        await using var child = factory.Begin(services);

        await child.SignalAsync(CommitOutcome.Committed);

        child
            .Coordinator.Invoking(x => x.OnCommit((_, _) => ValueTask.CompletedTask))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Commit scope already Committed.");

        child
            .Coordinator.Invoking(x => x.OnRollback((_, _) => ValueTask.CompletedTask))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Commit scope already Committed.");

        child
            .Coordinator.Invoking(x => x.GetOrAdd(_ => new DisposableBuffer()))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Commit scope already Committed.");
    }

    [Fact]
    public async Task should_doom_parent_when_child_rolls_back()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var services = new EmptyServiceProvider();
        var calls = 0;

        await using var parent = factory.Begin(services);
        parent.Coordinator.OnCommit(
            (_, _) =>
            {
                calls++;

                return ValueTask.CompletedTask;
            }
        );

        await using (var child = factory.Begin(services))
        {
            await child.SignalAsync(CommitOutcome.RolledBack);
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
            scope.Coordinator.OnCommit(
                (_, _) =>
                {
                    calls++;

                    return ValueTask.CompletedTask;
                }
            );
        }

        calls.Should().Be(0);
    }

    [Fact]
    public async Task should_run_rollback_callbacks_when_sync_scope_is_disposed_without_signal()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var services = new EmptyServiceProvider();
        var rollbackRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        await using (var scope = factory.Begin(services))
        {
            scope.Coordinator.OnRollback(
                (_, _) =>
                {
                    calls++;
                    rollbackRan.SetResult();

                    return ValueTask.CompletedTask;
                }
            );
        }

        await rollbackRan.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
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

                scope.Coordinator.OnRollback(
                    async (_, _) =>
                    {
                        // Posts the continuation back to the captured SynchronizationContext; a sync-over-async drain
                        // on the disposing thread would deadlock here unless the drain is offloaded.
                        await Task.Yield();
                        ran = true;
                    }
                );
            },
            TimeSpan.FromSeconds(10)
        );

        completed
            .Should()
            .BeTrue("sync Dispose must offload the rollback drain off the captured SynchronizationContext");
        SpinWait.SpinUntil(() => ran, TimeSpan.FromSeconds(5)).Should().BeTrue();
    }

    [Fact]
    public async Task should_not_roll_back_committed_work_when_disposed_before_the_commit_drain_completes()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var services = new EmptyServiceProvider();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committed = false;
        var rolledBack = false;

        var scope = factory.Begin(services);
        scope.Coordinator.OnCommit(
            async (_, _) =>
            {
                await gate.Task;
                committed = true;
            }
        );
        scope.Coordinator.OnRollback(
            (_, _) =>
            {
                rolledBack = true;

                return ValueTask.CompletedTask;
            }
        );

        // Claim the commit and start the drain; it blocks on the gate, so the drain is still in flight.
        var drain = scope.SignalAsync(CommitOutcome.Committed);

        // Dispose while the commit drain is pending. The terminal outcome was claimed synchronously by the signal,
        // so disposal must observe it and NOT roll back the committed work.
        await scope.DisposeAsync();

        rolledBack.Should().BeFalse();
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
        committed.Should().BeFalse("the drain is still gated");

        // Release the drain and confirm the committed work runs to completion.
        gate.SetResult();
        await drain;
        committed.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_dispose_child_promoted_rollback_work_before_root_drain_reaches_it()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var services = new EmptyServiceProvider();
        var rootDrainStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unblockRootDrain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var childRollbackRan = false;

        await using var parent = factory.Begin(services);
        parent.Coordinator.OnRollback(
            async (_, _) =>
            {
                rootDrainStarted.SetResult();
                await unblockRootDrain.Task;
            }
        );

        await using var child = factory.Begin(services);
        child.Coordinator.OnRollback(
            (_, _) =>
            {
                childRollbackRan = true;

                return ValueTask.CompletedTask;
            }
        );

        var drain = child.SignalAsync(CommitOutcome.RolledBack);
        await rootDrainStarted.Task;

        await child.DisposeAsync();
        childRollbackRan.Should().BeFalse("the root drain is still blocked before the child callback");

        unblockRootDrain.SetResult();
        await drain;

        childRollbackRan.Should().BeTrue("child rollback work promoted to the root must survive child disposal");
    }

    [Fact]
    public async Task should_pop_ambient_once_and_not_re_signal_when_disposed_after_signaling()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var services = new EmptyServiceProvider();
        var commits = 0;

        var scope = factory.Begin(services);
        scope.Coordinator.OnCommit(
            (_, _) =>
            {
                Interlocked.Increment(ref commits);

                return ValueTask.CompletedTask;
            }
        );

        await scope.SignalAsync(CommitOutcome.Committed);
        stack.Current.Should().NotBeNull("the ambient frame is owned by disposal, not by the signal");

        await scope.DisposeAsync();

        commits.Should().Be(1, "disposal after a signal must not drain a second terminal outcome");
        stack.Current.Should().BeNull();
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public void should_open_independent_root_when_begin_new_ambient_scope_active()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var services = new EmptyServiceProvider();

        using var ambient = factory.Begin(services);
        using var independent = factory.BeginNew(services);

        var ambientCoordinator = (CommitCoordinator)ambient.Coordinator;
        var independentCoordinator = (CommitCoordinator)independent.Coordinator;

        independentCoordinator.Should().NotBeSameAs(ambientCoordinator);
        independentCoordinator
            .Root.Should()
            .BeSameAs(
                independentCoordinator,
                "BeginNew opens an independent root, not a child joined to the ambient coordinator"
            );
    }

    [Fact]
    public void should_preserve_nested_physical_transaction_capabilities_when_signal_source_attach()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        using var services = new ServiceCollection().BuildServiceProvider();
        var outerCapability = new TestCapability();
        var innerCapability = new TestCapability();
        using var outer = factory.BeginNew(services, [outerCapability]);
        var scopes = new ConcurrentDictionary<object, ICommitScope>();
        var key = new object();

        using var inner = CommitSignalSourceAttach.Attach(
            factory,
            new CommitCoordinatorBindings
            {
                Services = services,
                Capabilities = [innerCapability],
                ProviderTransactionKey = key,
            },
            scopes,
            _ => new InvalidOperationException("duplicate"),
            CancellationToken.None
        );

        inner.Coordinator.TryGetCapability<TestCapability>(out var resolved).Should().BeTrue();
        resolved.Should().BeSameAs(innerCapability);
        outer.Coordinator.TryGetCapability<TestCapability>(out var outerResolved).Should().BeTrue();
        outerResolved.Should().BeSameAs(outerCapability);
    }

    [Fact]
    public void should_throw_when_pop_handle_outer_scope_disposed_before_inner()
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

    private sealed class DisposableBuffer : ICommitWorkBuffer, IDisposable
    {
        public void Dispose() { }
    }

    private sealed class TestCapability : ICommitCapability;
}
