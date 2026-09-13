// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class CommitCoordinatorTests : TestBase
{
    [Fact]
    public async Task should_reject_unspecified_outcome_before_claiming_coordinator_or_draining_callbacks()
    {
        var coordinator = new CommitCoordinator();
        var calls = 0;
        coordinator.OnCommit(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        Action act = () => coordinator.TryClaimTerminal(CommitOutcome.Unspecified, out _);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("outcome");
        coordinator.State.Should().Be(CommitCoordinatorState.Active);
        calls.Should().Be(0);

        await coordinator.SignalAsync(CommitOutcome.Committed);

        calls.Should().Be(1);
    }

    [Fact]
    public void should_keep_commit_enum_numeric_contracts_stable()
    {
        new[]
        {
            (int)CommitCoordinatorState.Active,
            (int)CommitCoordinatorState.Committed,
            (int)CommitCoordinatorState.RolledBack,
        }
            .Should()
            .Equal(0, 1, 2);

        new[] { (int)CommitOutcome.Unspecified, (int)CommitOutcome.Committed, (int)CommitOutcome.RolledBack }
            .Should()
            .Equal(0, 1, 2);
    }

    [Fact]
    public void should_expose_the_relational_handle_it_was_opened_with()
    {
        var relational = new StubRelationalCommitContext();

        new CommitCoordinator().Relational.Should().BeNull();
        new CommitCoordinator(relational).Relational.Should().BeSameAs(relational);
    }

    [Fact]
    public async Task should_drain_commit_callbacks_once_when_commit_is_signaled()
    {
        var coordinator = new CommitCoordinator();
        var calls = 0;

        coordinator.OnCommit(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await coordinator.SignalAsync(CommitOutcome.Committed);
        await coordinator.SignalAsync(CommitOutcome.Committed);

        calls.Should().Be(1);
        coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public async Task should_discard_commit_callbacks_and_dispose_scope_state_when_rollback_is_signaled()
    {
        var coordinator = new CommitCoordinator();
        var calls = 0;
        var state = coordinator.GetOrAdd(static _ => new DisposableState());

        coordinator.OnCommit(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await coordinator.SignalAsync(CommitOutcome.RolledBack);

        calls.Should().Be(0);
        state.IsDisposed.Should().BeTrue();
        coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
    }

    [Fact]
    public async Task should_run_remaining_callbacks_and_throw_aggregate_when_multiple_callbacks_fail()
    {
        var coordinator = new CommitCoordinator();
        var secondRan = false;

        coordinator.OnCommit(() => throw new InvalidOperationException("first"));
        coordinator.OnCommit(() =>
        {
            secondRan = true;

            throw new NotSupportedException("second");
        });

        var act = () => coordinator.SignalAsync(CommitOutcome.Committed).AsTask();

        var exception = await act.Should().ThrowAsync<AggregateException>();
        exception.Which.InnerExceptions.Should().HaveCount(2);
        secondRan.Should().BeTrue();
        coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public async Task should_throw_when_enlisting_after_terminal_state()
    {
        var coordinator = new CommitCoordinator();

        await coordinator.SignalAsync(CommitOutcome.Committed);

        coordinator
            .Invoking(x => x.OnCommit(() => ValueTask.CompletedTask))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Commit scope already Committed.");
        coordinator
            .Invoking(x => x.GetOrAdd(static _ => new DisposableState()))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Commit scope already Committed.");
    }

    [Fact]
    public async Task should_throw_when_a_callback_registers_another_callback_during_the_drain()
    {
        var coordinator = new CommitCoordinator();
        var nestedCalls = 0;

        coordinator.OnCommit(() =>
        {
            // The state is already terminal when the drain runs, so a late registration cannot be silently
            // accepted-but-never-drained; it must throw like any other post-terminal enlist.
            coordinator.OnCommit(() =>
            {
                nestedCalls++;

                return ValueTask.CompletedTask;
            });

            return ValueTask.CompletedTask;
        });

        var act = () => coordinator.SignalAsync(CommitOutcome.Committed).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Commit scope already Committed.");
        nestedCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_deregister_callback_when_handle_is_disposed()
    {
        var coordinator = new CommitCoordinator();
        var calls = 0;

        using (
            coordinator.OnCommit(() =>
            {
                calls++;

                return ValueTask.CompletedTask;
            })
        ) { }

        await coordinator.SignalAsync(CommitOutcome.Committed);

        calls.Should().Be(0);
    }

    [Fact]
    public async Task should_dispose_scope_state_after_the_commit_drain()
    {
        var coordinator = new CommitCoordinator();
        var state = coordinator.GetOrAdd(static _ => new DisposableState());
        var disposedDuringCallback = true;

        coordinator.OnCommit(() =>
        {
            disposedDuringCallback = state.IsDisposed;

            return ValueTask.CompletedTask;
        });

        await coordinator.SignalAsync(CommitOutcome.Committed);

        disposedDuringCallback.Should().BeFalse("state outlives the callbacks that use it");
        state.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void should_pass_arg_to_get_or_add_factory_and_reuse_existing_state()
    {
        var coordinator = new CommitCoordinator();
        var calls = 0;

        var first = coordinator.GetOrAdd(
            new FactoryArg("first", () => ++calls),
            static (_, arg) => new StatefulEntry(arg.Value, arg.Next())
        );
        var second = coordinator.GetOrAdd(
            new FactoryArg("second", () => ++calls),
            static (_, arg) => new StatefulEntry(arg.Value, arg.Next())
        );

        first.Should().BeSameAs(second);
        first.State.Should().Be("first");
        first.CallsAtCreation.Should().Be(1);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task should_reach_terminal_state_when_state_disposal_fails()
    {
        var coordinator = new CommitCoordinator();
        coordinator.GetOrAdd(static _ => new ThrowingDisposableState());
        coordinator.OnCommit(() => throw new NotSupportedException("callback"));

        var act = () => coordinator.SignalAsync(CommitOutcome.Committed).AsTask();

        var exception = await act.Should().ThrowAsync<AggregateException>();
        exception.Which.InnerExceptions.Should().ContainSingle(x => x.Message == "callback");
        exception.Which.InnerExceptions.Should().ContainSingle(x => x.Message == "dispose");
        coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public async Task should_ignore_repeated_same_outcome_signal_silently()
    {
        var logger = new CapturingLogger<CommitCoordinator>();
        var coordinator = new CommitCoordinator(logger: logger);

        await coordinator.SignalAsync(CommitOutcome.RolledBack);
        await coordinator.SignalAsync(CommitOutcome.RolledBack);

        coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_ignore_and_log_conflicting_later_signal()
    {
        var logger = new CapturingLogger<CommitCoordinator>();
        var coordinator = new CommitCoordinator(logger: logger);
        var calls = 0;

        coordinator.OnCommit(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await coordinator.SignalAsync(CommitOutcome.RolledBack);
        await coordinator.SignalAsync(CommitOutcome.Committed);

        calls.Should().Be(0);
        coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);

        var warning = logger.Entries.Should().ContainSingle().Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Be("Commit scope already RolledBack; ignoring conflicting Committed signal.");
    }

    [Fact]
    public async Task should_not_claim_or_log_when_abandon_follows_a_signal()
    {
        var logger = new CapturingLogger<CommitCoordinator>();
        var coordinator = new CommitCoordinator(logger: logger);

        await coordinator.SignalAsync(CommitOutcome.Committed);

        // Dispose-after-signal is the normal lifecycle, not a conflicting signal: no claim, no warning.
        coordinator.TryClaimAbandon(out _).Should().BeFalse();
        coordinator.State.Should().Be(CommitCoordinatorState.Committed);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_claim_rollback_and_capture_state_when_abandoned_while_active()
    {
        var coordinator = new CommitCoordinator();
        var state = coordinator.GetOrAdd(static _ => new DisposableState());
        var calls = 0;

        coordinator.OnCommit(() =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        coordinator.TryClaimAbandon(out var claim).Should().BeTrue();
        coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
        claim.Callbacks.Should().BeEmpty("rollback never runs commit work");

        await CommitCoordinator.DrainAsync(claim);

        calls.Should().Be(0);
        state.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task should_log_when_a_background_drain_faults()
    {
        var logger = new CapturingLogger<CommitCoordinator>();
        var coordinator = new CommitCoordinator(logger: logger);
        coordinator.GetOrAdd(static _ => new ThrowingDisposableState());
        coordinator.TryClaimAbandon(out var claim).Should().BeTrue();

        CommitCoordinator.DrainInBackground(claim);

        await _WaitUntilAsync(() => logger.Entries.Count > 0);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Be("A commit coordination background drain faulted.");
    }

    [Fact]
    public async Task should_drain_every_accepted_enlist_exactly_once_under_concurrent_enlist_and_signal()
    {
        // Concurrent enlist during the Active -> terminal transition: every OnCommit that is accepted (does not throw)
        // must be drained exactly once; every enlist that loses the race to the terminal claim must throw
        // InvalidOperationException -- never a silent strand (accepted but never drained).
        const int enlisters = 24;

        for (var iteration = 0; iteration < 150; iteration++)
        {
            var coordinator = new CommitCoordinator();
            var drained = 0;
            var accepted = 0;
            using var barrier = new Barrier(enlisters + 1);

            var tasks = new Task[enlisters + 1];

            // Every participant runs on a dedicated thread: 25 participants blocked in the barrier on pool threads
            // starve the pool (~1 thread injected per second), which stretched these 150 iterations to ~18s.
            for (var i = 0; i < enlisters; i++)
            {
                tasks[i] = Task.Factory.StartNew(
                    () =>
                    {
                        barrier.SignalAndWait();

                        try
                        {
                            coordinator.OnCommit(() =>
                            {
                                Interlocked.Increment(ref drained);

                                return ValueTask.CompletedTask;
                            });

                            Interlocked.Increment(ref accepted);
                        }
                        catch (InvalidOperationException)
                        {
                            // Lost the race to the terminal transition: enlist-after-terminal throws (never a strand).
                        }
                    },
                    AbortToken,
                    TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                );
            }

            tasks[enlisters] = Task
                .Factory.StartNew(
                    async () =>
                    {
                        barrier.SignalAndWait();

                        await coordinator.SignalAsync(CommitOutcome.Committed);
                    },
                    AbortToken,
                    TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                )
                .Unwrap();

            await Task.WhenAll(tasks);

            coordinator.State.Should().Be(CommitCoordinatorState.Committed);
            drained.Should().Be(accepted, "every accepted enlist must drain exactly once with no silent strand");
        }
    }

    private static async Task _WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The awaited condition did not become true.");
            }

            await Task.Delay(10, AbortToken);
        }
    }

    private sealed class DisposableState : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class ThrowingDisposableState : IDisposable
    {
        public void Dispose()
        {
            throw new InvalidOperationException("dispose");
        }
    }

    private sealed record FactoryArg(string Value, Func<int> Next);

    private sealed record StatefulEntry(string State, int CallsAtCreation);
}
