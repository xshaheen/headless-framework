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

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
