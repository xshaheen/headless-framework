// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Testing.Tests;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Provider-agnostic acceptance scenarios for the commit coordinator (§8). Every scenario here is
/// portable across providers; provider-specific concerns (logger capture, capabilities, durable
/// buffers, synchronization-context behavior) live in the concrete runner instead of this base.
/// </summary>
public abstract class CommitCoordinationConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : ICommitCoordinationFixture
{
    public virtual async Task should_run_commit_work_once_after_commit_signal()
    {
        await using var scope = await fixture.BeginScopeAsync(AbortToken);
        var calls = 0;

        scope.Coordinator.OnCommit(
            (context, _) =>
            {
                context.Outcome.Should().Be(CommitOutcome.Committed);
                calls++;

                return ValueTask.CompletedTask;
            }
        );

        await scope.SignalAsync(CommitOutcome.Committed);
        await scope.SignalAsync(CommitOutcome.Committed);

        calls.Should().Be(1);
    }

    public virtual async Task should_discard_commit_work_after_rollback_signal()
    {
        await using var scope = await fixture.BeginScopeAsync(AbortToken);
        var calls = 0;

        scope.Coordinator.OnCommit(
            (_, _) =>
            {
                calls++;

                return ValueTask.CompletedTask;
            }
        );

        await scope.SignalAsync(CommitOutcome.RolledBack);

        calls.Should().Be(0);
    }

    public virtual async Task should_reject_enlistment_after_terminal_signal()
    {
        await using var scope = await fixture.BeginScopeAsync(AbortToken);

        await scope.SignalAsync(CommitOutcome.Committed);

        scope
            .Coordinator.Invoking(x => x.OnCommit((_, _) => ValueTask.CompletedTask))
            .Should()
            .Throw<InvalidOperationException>();
    }

    public virtual async Task should_promote_child_commit_work_to_root_when_root_commits()
    {
        var factory = new CommitScopeFactory((CommitScopeStack)fixture.CreateStack());

        await using var root = factory.Begin(fixture.Services);
        var calls = 0;

        await using (var child = factory.Begin(fixture.Services))
        {
            child.Coordinator.OnCommit(
                (_, _) =>
                {
                    calls++;

                    return ValueTask.CompletedTask;
                }
            );

            // Child commit signal only marks the child; the root drives the actual drain.
            await child.SignalAsync(CommitOutcome.Committed);

            // Disposing the child alone must not fire promoted work.
            calls.Should().Be(0);
        }

        calls.Should().Be(0);

        await root.SignalAsync(CommitOutcome.Committed);

        calls.Should().Be(1);
    }

    public virtual async Task should_doom_root_and_discard_all_work_when_child_rolls_back()
    {
        var factory = new CommitScopeFactory((CommitScopeStack)fixture.CreateStack());

        await using var root = factory.Begin(fixture.Services);
        var rootCalls = 0;
        var childCalls = 0;

        root.Coordinator.OnCommit(
            (_, _) =>
            {
                rootCalls++;

                return ValueTask.CompletedTask;
            }
        );

        await using (var child = factory.Begin(fixture.Services))
        {
            child.Coordinator.OnCommit(
                (_, _) =>
                {
                    childCalls++;

                    return ValueTask.CompletedTask;
                }
            );

            await child.SignalAsync(CommitOutcome.RolledBack);
        }

        root.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
        rootCalls.Should().Be(0);
        childCalls.Should().Be(0);
    }

    public virtual async Task should_discard_promoted_child_work_when_root_rolls_back()
    {
        var factory = new CommitScopeFactory((CommitScopeStack)fixture.CreateStack());

        await using var root = factory.Begin(fixture.Services);
        var calls = 0;

        await using (var child = factory.Begin(fixture.Services))
        {
            child.Coordinator.OnCommit(
                (_, _) =>
                {
                    calls++;

                    return ValueTask.CompletedTask;
                }
            );
        }

        await root.SignalAsync(CommitOutcome.RolledBack);

        calls.Should().Be(0);
    }

    public virtual async Task should_doom_root_when_child_disposed_without_signal()
    {
        var factory = new CommitScopeFactory((CommitScopeStack)fixture.CreateStack());

        await using var root = factory.Begin(fixture.Services);
        var rootCalls = 0;

        root.Coordinator.OnCommit(
            (_, _) =>
            {
                rootCalls++;

                return ValueTask.CompletedTask;
            }
        );

        // Disposing a CHILD scope WITHOUT signalling is an implicit rollback that dooms the ROOT — not merely a
        // discard of the child's own promoted work. The root must be RolledBack the moment the child's using block
        // exits, before any root signal. (await using drains the doomed root's rollback inline, so this is
        // deterministic — no background-timing race.)
        await using (factory.Begin(fixture.Services)) { }

        root.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);

        // The root's own later commit is now an ignored racing signal; its registered commit work never runs.
        await root.SignalAsync(CommitOutcome.Committed);

        rootCalls.Should().Be(0);
    }

    public virtual async Task should_preserve_parent_slot_when_concurrent_child_flows_fork_and_dispose()
    {
        var stack = (CommitScopeStack)fixture.CreateStack();
        var factory = new CommitScopeFactory(stack);

        await using var root = factory.Begin(fixture.Services);
        var rootCoordinator = root.Coordinator;

        async Task forkAsync()
        {
            // Each branch forks the AsyncLocal copy-on-write frame; out-of-order disposal across
            // branches must not corrupt the parent's ambient slot.
            await using var child = factory.Begin(fixture.Services);
            await Task.Yield();
            await child.SignalAsync(CommitOutcome.Committed);
        }

        await Task.WhenAll(forkAsync(), forkAsync());

        stack.Current.Should().BeSameAs(rootCoordinator);
    }

    public virtual async Task should_run_remaining_callbacks_and_surface_fault_when_first_commit_callback_throws()
    {
        await using var scope = await fixture.BeginScopeAsync(AbortToken);
        var secondRan = false;
        CancellationToken? observedToken = null;

        scope.Coordinator.OnCommit((_, _) => throw new InvalidOperationException("boom"));
        scope.Coordinator.OnCommit(
            (_, token) =>
            {
                secondRan = true;
                observedToken = token;

                return ValueTask.CompletedTask;
            }
        );

        var act = () => scope.SignalAsync(CommitOutcome.Committed).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        secondRan.Should().BeTrue();
        // Terminal callbacks must run under CancellationToken.None regardless of the signal token.
        observedToken.Should().NotBeNull();
        observedToken!.Value.CanBeCanceled.Should().BeFalse();
        observedToken.Value.Should().Be(CancellationToken.None);
    }

    public virtual async Task should_discard_work_when_scope_is_disposed_without_signal()
    {
        var factory = new CommitScopeFactory((CommitScopeStack)fixture.CreateStack());
        var calls = 0;

        await using (var scope = factory.Begin(fixture.Services))
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
}
