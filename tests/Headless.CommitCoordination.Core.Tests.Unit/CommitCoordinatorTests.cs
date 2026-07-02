// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Testing.Tests;

namespace Tests;

public sealed class CommitCoordinatorTests : TestBase
{
    [Fact]
    public async Task should_drain_commit_callbacks_once_when_commit_is_signaled()
    {
        var coordinator = new CommitCoordinator();
        var calls = 0;

        coordinator.OnCommit(
            (context, _) =>
            {
                context.Outcome.Should().Be(CommitOutcome.Committed);
                calls++;

                return ValueTask.CompletedTask;
            }
        );

        await coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider());
        await coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider());

        calls.Should().Be(1);
        coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public async Task should_discard_commit_callbacks_when_rollback_is_signaled()
    {
        var coordinator = new CommitCoordinator();
        var commitCalls = 0;
        var rollbackCalls = 0;

        coordinator.OnCommit(
            (_, _) =>
            {
                commitCalls++;

                return ValueTask.CompletedTask;
            }
        );

        coordinator.OnRollback(
            (_, _) =>
            {
                rollbackCalls++;

                return ValueTask.CompletedTask;
            }
        );

        await coordinator.SignalAsync(CommitOutcome.RolledBack, new EmptyServiceProvider());

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
        coordinator.OnCommit(
            (_, _) =>
            {
                secondRan = true;

                throw new NotSupportedException("second");
            }
        );

        var act = () => coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider()).AsTask();

        var exception = await act.Should().ThrowAsync<AggregateException>();
        exception.Which.InnerExceptions.Should().HaveCount(2);
        secondRan.Should().BeTrue();
    }

    [Fact]
    public async Task should_run_drain_to_completion_and_surface_callback_faults_even_when_a_token_is_canceled_mid_drain()
    {
        var coordinator = new CommitCoordinator();
        using var cts = new CancellationTokenSource();

        coordinator.OnCommit(
            async (_, _) =>
            {
                // Cancelling a token from inside the drain must not abandon the drain or suppress the fault (D9): the
                // drain takes no cancellation token, so it always runs to completion and the callback fault propagates.
                await cts.CancelAsync();

                throw new InvalidOperationException("callback failed");
            }
        );

        var act = () => coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider()).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("callback failed");
        coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public async Task should_throw_when_enlisting_after_terminal_state()
    {
        var coordinator = new CommitCoordinator();

        await coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider());

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

        using (
            coordinator.OnCommit(
                (_, _) =>
                {
                    calls++;

                    return ValueTask.CompletedTask;
                }
            )
        ) { }

        await coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider());

        calls.Should().Be(0);
    }

    [Fact]
    public async Task should_dispose_scope_buffers_on_terminal_signal()
    {
        var coordinator = new CommitCoordinator();
        var buffer = coordinator.GetOrAdd(_ => new DisposableBuffer());

        await coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider());

        buffer.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void should_pass_state_to_get_or_add_factory_and_reuse_existing_buffer()
    {
        var coordinator = new CommitCoordinator();
        var calls = 0;

        var first = coordinator.GetOrAdd(
            new BufferFactoryState("first", () => ++calls),
            static (_, state) => new StatefulBuffer(state.Value, state.Next())
        );
        var second = coordinator.GetOrAdd(
            new BufferFactoryState("second", () => ++calls),
            static (_, state) => new StatefulBuffer(state.Value, state.Next())
        );

        first.Should().BeSameAs(second);
        first.State.Should().Be("first");
        first.CallsAtCreation.Should().Be(1);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task should_reach_terminal_state_when_buffer_disposal_fails()
    {
        var coordinator = new CommitCoordinator();
        coordinator.GetOrAdd(_ => new ThrowingDisposableBuffer());
        coordinator.OnCommit((_, _) => throw new NotSupportedException("callback"));

        var act = () => coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider()).AsTask();

        var exception = await act.Should().ThrowAsync<AggregateException>();
        exception.Which.InnerExceptions.Should().ContainSingle(x => x.Message == "callback");
        exception.Which.InnerExceptions.Should().ContainSingle(x => x.Message == "dispose");
        coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public async Task should_drain_every_accepted_enlist_exactly_once_under_concurrent_enlist_and_signal()
    {
        // Acceptance criterion (plan section 8): concurrent enlist during the Active -> terminal transition. Every
        // OnCommit that is accepted (does not throw) must be drained exactly once; every enlist that loses the race to
        // the terminal claim must throw InvalidOperationException -- never a silent strand (accepted but never drained).
        const int enlisters = 24;

        for (var iteration = 0; iteration < 150; iteration++)
        {
            var coordinator = new CommitCoordinator();
            var drained = 0;
            var accepted = 0;
            using var barrier = new Barrier(enlisters + 1);

            var tasks = new Task[enlisters + 1];

            for (var i = 0; i < enlisters; i++)
            {
                tasks[i] = Task.Run(
                    () =>
                    {
                        barrier.SignalAndWait();

                        try
                        {
                            coordinator.OnCommit(
                                (_, _) =>
                                {
                                    Interlocked.Increment(ref drained);

                                    return ValueTask.CompletedTask;
                                }
                            );

                            Interlocked.Increment(ref accepted);
                        }
                        catch (InvalidOperationException)
                        {
                            // Lost the race to the terminal transition: enlist-after-terminal throws (never a strand).
                        }
                    },
                    AbortToken
                );
            }

            tasks[enlisters] = Task.Run(
                async () =>
                {
                    barrier.SignalAndWait();

                    await coordinator.SignalAsync(CommitOutcome.Committed, new EmptyServiceProvider());
                },
                AbortToken
            );

            await Task.WhenAll(tasks);

            coordinator.State.Should().Be(CommitCoordinatorState.Committed);
            drained.Should().Be(accepted, "every accepted enlist must drain exactly once with no silent strand");
        }
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

    private sealed class ThrowingDisposableBuffer : ICommitWorkBuffer, IDisposable
    {
        public void Dispose()
        {
            throw new InvalidOperationException("dispose");
        }
    }

    [Fact]
    public async Task drain_then_should_propagate_the_drain_fault_when_only_the_drain_throws()
    {
        var coordinator = new CommitCoordinator();
        coordinator.OnCommit((_, _) => throw new InvalidOperationException("drain boom"));
        coordinator.TryClaimTerminal(CommitOutcome.Committed, out var claim).Should().BeTrue();

        var act = async () =>
            await CommitCoordinator.DrainThenAsync(claim, new EmptyServiceProvider(), afterDrain: null);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("drain boom");
    }

    [Fact]
    public async Task drain_then_should_propagate_the_after_drain_fault_when_only_after_drain_throws()
    {
        var coordinator = new CommitCoordinator();
        coordinator.OnCommit((_, _) => ValueTask.CompletedTask);
        coordinator.TryClaimTerminal(CommitOutcome.Committed, out var claim).Should().BeTrue();

        var act = async () =>
            await CommitCoordinator.DrainThenAsync(
                claim,
                new EmptyServiceProvider(),
                () => throw new InvalidOperationException("cleanup boom")
            );

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("cleanup boom");
    }

    [Fact]
    public async Task drain_then_should_surface_both_faults_as_aggregate_with_drain_first_when_both_throw()
    {
        // The #2 fix: a cleanup (afterDrain) fault must NOT mask the drain fault. Both surface via
        // AggregateException with the drain fault as the first inner.
        var coordinator = new CommitCoordinator();
        coordinator.OnCommit((_, _) => throw new InvalidOperationException("drain boom"));
        coordinator.TryClaimTerminal(CommitOutcome.Committed, out var claim).Should().BeTrue();

        var act = async () =>
            await CommitCoordinator.DrainThenAsync(
                claim,
                new EmptyServiceProvider(),
                () => throw new InvalidOperationException("cleanup boom")
            );

        var aggregate = (await act.Should().ThrowAsync<AggregateException>()).Which;
        aggregate.InnerExceptions.Should().HaveCount(2);
        aggregate.InnerExceptions[0].Message.Should().Be("drain boom");
        aggregate.InnerExceptions[1].Message.Should().Be("cleanup boom");
    }

    private sealed record BufferFactoryState(string Value, Func<int> Next);

    private sealed record StatefulBuffer(string State, int CallsAtCreation) : ICommitWorkBuffer;

    private interface ITestCapability : ICommitCapability;

    private sealed class TestCapability : ITestCapability;
}
