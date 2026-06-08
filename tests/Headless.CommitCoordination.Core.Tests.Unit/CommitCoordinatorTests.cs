// Copyright (c) Mahmoud Shaheen. All rights reserved.

using AwesomeAssertions;
using Headless.CommitCoordination;

namespace Tests;

public sealed class CommitCoordinatorTests
{
    [Fact]
    public async Task should_drain_commit_callbacks_once_when_commit_is_signaled()
    {
        var coordinator = new CommitCoordinator();
        var calls = 0;

        coordinator.OnCommit((context, _) =>
        {
            context.Outcome.Should().Be(CommitOutcome.Committed);
            calls++;

            return ValueTask.CompletedTask;
        });

        await coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider(), CancellationToken.None);
        await coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider(), CancellationToken.None);

        calls.Should().Be(1);
        coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public async Task should_discard_commit_callbacks_when_rollback_is_signaled()
    {
        var coordinator = new CommitCoordinator();
        var commitCalls = 0;
        var rollbackCalls = 0;

        coordinator.OnCommit((_, _) =>
        {
            commitCalls++;

            return ValueTask.CompletedTask;
        });

        coordinator.OnRollback((_, _) =>
        {
            rollbackCalls++;

            return ValueTask.CompletedTask;
        });

        await coordinator.SignalAsync(CommitOutcome.RolledBack, new EmptyServiceProvider(), CancellationToken.None);

        commitCalls.Should().Be(0);
        rollbackCalls.Should().Be(1);
        coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
    }

    [Fact]
    public async Task should_run_remaining_callbacks_and_throw_aggregate_when_multiple_callbacks_fail()
    {
        var coordinator = new CommitCoordinator();
        var secondRan = false;

        coordinator.OnCommit((_, _) => throw new InvalidOperationException("first"));
        coordinator.OnCommit((_, _) =>
        {
            secondRan = true;

            throw new NotSupportedException("second");
        });

        var act = () => coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider(), CancellationToken.None).AsTask();

        var exception = await act.Should().ThrowAsync<AggregateException>();
        exception.Which.InnerExceptions.Should().HaveCount(2);
        secondRan.Should().BeTrue();
    }

    [Fact]
    public async Task should_throw_when_enlisting_after_terminal_state()
    {
        var coordinator = new CommitCoordinator();

        await coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider(), CancellationToken.None);

        coordinator
            .Invoking(x => x.OnCommit((_, _) => ValueTask.CompletedTask))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Commit scope already Committed.");
    }

    [Fact]
    public async Task should_deregister_callback_when_handle_is_disposed()
    {
        var coordinator = new CommitCoordinator();
        var calls = 0;

        using (coordinator.OnCommit((_, _) =>
        {
            calls++;

            return ValueTask.CompletedTask;
        }))
        {
        }

        await coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider(), CancellationToken.None);

        calls.Should().Be(0);
    }

    [Fact]
    public async Task should_dispose_scope_buffers_on_terminal_signal()
    {
        var coordinator = new CommitCoordinator();
        var buffer = coordinator.GetOrAdd(_ => new DisposableBuffer());

        await coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider(), CancellationToken.None);

        buffer.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void should_resolve_capability_by_contract()
    {
        var capability = new TestCapability();
        var coordinator = new CommitCoordinator([capability]);

        var result = coordinator.TryGetCapability<ITestCapability>(out var resolved);

        result.Should().BeTrue();
        resolved.Should().BeSameAs(capability);
    }

    private sealed class DisposableBuffer : ICommitWorkBuffer, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private interface ITestCapability : ICommitCapability;

    private sealed class TestCapability : ITestCapability;

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
