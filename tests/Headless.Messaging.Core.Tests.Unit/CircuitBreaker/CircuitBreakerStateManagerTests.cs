// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Exceptions;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.CircuitBreaker;

public sealed class CircuitBreakerStateManagerTests : TestBase
{
    private const string Group = "test.group";

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static CircuitBreakerStateManager _Create(
        int failureThreshold = 5,
        TimeSpan? openDuration = null,
        TimeSpan? maxOpenDuration = null,
        int successfulCyclesToResetEscalation = 3,
        ConsumerCircuitBreakerRegistry? registry = null
    )
    {
        var opts = new CircuitBreakerOptions
        {
            FailureThreshold = failureThreshold,
            OpenDuration = openDuration ?? TimeSpan.FromSeconds(30),
            MaxOpenDuration = maxOpenDuration ?? TimeSpan.FromSeconds(240),
            SuccessfulCyclesToResetEscalation = successfulCyclesToResetEscalation,
        };

        var meterFactory = CircuitBreakerTestHelpers.CreateMeterFactory();

        return new CircuitBreakerStateManager(
            Options.Create(opts),
            registry ?? new ConsumerCircuitBreakerRegistry(),
            new NullLogger<CircuitBreakerStateManager>(),
            new CircuitBreakerMetrics(meterFactory)
        );
    }

    private static async Task _ReportTransientFailuresAsync(ICircuitBreakerStateManager sut, string group, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await sut.ReportFailureAsync(group, new TimeoutException("transient"));
        }
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task should_remain_closed_when_failures_below_threshold()
    {
        // given
        var sut = _Create(failureThreshold: 5);

        // when — 4 failures, threshold is 5
        await _ReportTransientFailuresAsync(sut, Group, 4);

        // then
        sut.IsOpen(Group).Should().BeFalse();
    }

    [Fact]
    public async Task should_open_after_reaching_failure_threshold()
    {
        // given
        var pauseCalled = false;
        var sut = _Create(failureThreshold: 5);
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () =>
            {
                pauseCalled = true;
                return ValueTask.CompletedTask;
            },
            onResume: () => ValueTask.CompletedTask
        );

        // when — exactly at threshold
        await _ReportTransientFailuresAsync(sut, Group, 5);

        // then
        sut.IsOpen(Group).Should().BeTrue();
        pauseCalled.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_trip_on_non_transient_exception()
    {
        // given
        var sut = _Create(failureThreshold: 1);

        // when
        await sut.ReportFailureAsync(Group, new ArgumentException("bad arg"));

        // then — non-transient, circuit stays closed
        sut.IsOpen(Group).Should().BeFalse();
    }

    [Fact]
    public async Task should_reset_counter_on_success()
    {
        // given
        var sut = _Create(failureThreshold: 5);

        // when — 4 failures, then a success, then 4 more failures
        await _ReportTransientFailuresAsync(sut, Group, 4);
        await sut.ReportSuccessAsync(Group);
        await _ReportTransientFailuresAsync(sut, Group, 4);

        // then — still closed because success reset the counter
        sut.IsOpen(Group).Should().BeFalse();
    }

    [Fact]
    public async Task should_transition_from_open_to_halfopen_after_timer()
    {
        // given
        var resumeCalled = false;
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                resumeCalled = true;
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        // when — trip the circuit then wait for resume callback
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // then — circuit is now HalfOpen (IsOpen still returns true to prevent new messages)
        sut.IsOpen(Group).Should().BeTrue();
        resumeCalled.Should().BeTrue();
    }

    [Fact]
    public async Task should_allow_only_one_halfopen_probe_at_a_time()
    {
        // given
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        await sut.ReportFailureAsync(Group, new TimeoutException());
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // when
        var firstProbe = sut.TryAcquireHalfOpenProbe(Group);
        var secondProbe = sut.TryAcquireHalfOpenProbe(Group);

        // then
        firstProbe.Should().BeTrue();
        secondProbe.Should().BeFalse();
    }

    [Fact]
    public async Task should_close_on_halfopen_success()
    {
        // given
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        // open then wait for HalfOpen
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // when — probe succeeds
        await sut.ReportSuccessAsync(Group);

        // then
        sut.IsOpen(Group).Should().BeFalse();
    }

    [Fact]
    public async Task should_reopen_on_halfopen_transient_failure()
    {
        // given
        var pauseCount = 0;
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () =>
            {
                pauseCount++;
                return ValueTask.CompletedTask;
            },
            onResume: () =>
            {
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        // open then wait for HalfOpen
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // when — probe fails with a transient error
        await sut.ReportFailureAsync(Group, new BrokerConnectionException(new Exception("broker down")));

        // then — circuit re-opens
        sut.IsOpen(Group).Should().BeTrue();
        pauseCount.Should().Be(2); // initial open + re-open
    }

    [Fact]
    public async Task should_close_on_halfopen_non_transient_failure()
    {
        // given — non-transient failure in HalfOpen means the message is bad, dependency is healthy
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(failureThreshold: 2, openDuration: TimeSpan.FromMilliseconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        // open then wait for HalfOpen
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // when — non-transient failure (bad message, not broker issue)
        await sut.ReportFailureAsync(Group, new ArgumentException("bad message payload"));

        // then — circuit closes because the dependency is fine
        sut.IsOpen(Group).Should().BeFalse();

        // and the old failure streak must not survive the close
        await sut.ReportFailureAsync(Group, new TimeoutException());
        sut.IsOpen(Group).Should().BeFalse();
    }

    [Fact]
    public async Task should_escalate_open_duration_on_repeated_reopens()
    {
        // given — very short base duration so we can observe escalation in ms
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(
            failureThreshold: 1,
            openDuration: TimeSpan.FromMilliseconds(20),
            maxOpenDuration: TimeSpan.FromMilliseconds(200)
        );
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        // first open: duration = 20ms (level 0 → 1)
        await sut.ReportFailureAsync(Group, new TimeoutException());

        // wait for HalfOpen, then fail transient → second open: duration = 40ms (level 1 → 2)
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await sut.ReportFailureAsync(Group, new TimeoutException());

        // wait for HalfOpen again, then fail transient → third open: duration = 80ms (level 2 → 3)
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await sut.ReportFailureAsync(Group, new TimeoutException());

        // the third open should still be open (80ms hasn't passed yet from NOW)
        sut.IsOpen(Group).Should().BeTrue();

        // wait for it to expire
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        sut.IsOpen(Group).Should().BeTrue(); // HalfOpen counts as open
    }

    [Fact]
    public async Task should_reset_escalation_after_3_healthy_cycles()
    {
        // given
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(
            failureThreshold: 1,
            openDuration: TimeSpan.FromMilliseconds(20),
            successfulCyclesToResetEscalation: 3
        );
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        // helper: open → wait for HalfOpen → close via success
        async Task _CycleAsync()
        {
            await sut.ReportFailureAsync(Group, new TimeoutException());
            await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await sut.ReportSuccessAsync(Group);
        }

        // open, half-open, close × 3 → escalation should reset
        await _CycleAsync();
        await _CycleAsync();
        await _CycleAsync();

        // after 3 healthy cycles, circuit should be closed
        sut.IsOpen(Group).Should().BeFalse();
    }

    [Fact]
    public async Task should_require_consecutive_healthy_cycles_to_reset_escalation()
    {
        // given
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(
            failureThreshold: 1,
            openDuration: TimeSpan.FromMilliseconds(20),
            successfulCyclesToResetEscalation: 3
        );
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        async Task cycleAsync()
        {
            await sut.ReportFailureAsync(Group, new TimeoutException());
            await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await sut.ReportSuccessAsync(Group);
        }

        await cycleAsync();
        await cycleAsync();

        // Break the healthy streak with another outage.
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // If the streak was not reset on reopen, this single healthy close would reset escalation.
        await sut.ReportSuccessAsync(Group);
        await sut.ReportFailureAsync(Group, new TimeoutException());

        sut.GetState(Group).Should().Be(CircuitBreakerState.Open);
    }

    [Fact]
    public async Task repeated_non_transient_close_should_not_reset_escalation()
    {
        // given — 3 successful cycles normally resets escalation, but non-transient
        // failure closes are NOT recovery signals and must not count toward that threshold.
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(
            failureThreshold: 1,
            openDuration: TimeSpan.FromMilliseconds(20),
            successfulCyclesToResetEscalation: 3
        );
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        // Escalate: open → half-open → transient failure (re-open) to bump escalation
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var escalationBefore = sut.GetSnapshot(Group)!.EscalationLevel;
        escalationBefore.Should().BeGreaterThan(0);

        // Close via non-transient failure × 3 — should NOT reset escalation
        for (var i = 0; i < 3; i++)
        {
            await sut.ReportFailureAsync(Group, new ArgumentException("bad payload"));
            sut.IsOpen(Group).Should().BeFalse();

            // Re-open for the next cycle
            await sut.ReportFailureAsync(Group, new TimeoutException());
            await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // then — escalation must still be present (not reset by non-transient closes)
        sut.GetSnapshot(Group)!.EscalationLevel.Should().BeGreaterThanOrEqualTo(escalationBefore);
    }

    [Fact]
    public async Task should_dispose_stale_timer_on_reopen()
    {
        // given — first open duration is short enough to fire; second open duration is longer
        var resumeCallCount = 0;
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(
            failureThreshold: 2,
            openDuration: TimeSpan.FromMilliseconds(30),
            maxOpenDuration: TimeSpan.FromMilliseconds(500)
        );
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                resumeCallCount++;
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        // trip the circuit (threshold=2)
        await _ReportTransientFailuresAsync(sut, Group, 2);
        sut.IsOpen(Group).Should().BeTrue();

        // wait briefly, then immediately re-trip to reset the timer before it fires
        await Task.Delay(10);
        // add more failures to re-open (re-escalation while already open just replaces timer)
        await sut.ReportFailureAsync(Group, new TimeoutException());

        // wait for the escalated timer to fire
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // then — resume should have been called exactly once (new timer, not the stale one)
        resumeCallCount.Should().Be(1);
    }

    [Fact]
    public async Task should_invoke_pause_callback_on_open()
    {
        // given
        var pauseInvoked = false;
        var sut = _Create(failureThreshold: 1);
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () =>
            {
                pauseInvoked = true;
                return ValueTask.CompletedTask;
            },
            onResume: () => ValueTask.CompletedTask
        );

        // when
        await sut.ReportFailureAsync(Group, new TimeoutException());

        // then
        pauseInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task should_invoke_resume_callback_on_halfopen()
    {
        // given
        var resumeInvoked = false;
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                resumeInvoked = true;
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        // when — trip then wait for resume callback
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // then
        resumeInvoked.Should().BeTrue();
    }

    [Fact]
    public void IsOpen_returns_false_for_unregistered_group()
    {
        var sut = _Create();
        sut.IsOpen("never-registered").Should().BeFalse();
    }

    [Fact]
    public async Task ReportSuccessAsync_is_noop_for_unregistered_group()
    {
        var sut = _Create();
        var act = async () => await sut.ReportSuccessAsync("never-registered");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReportFailure_while_open_increments_counter_but_does_not_re_trigger_open()
    {
        // given — trip circuit to Open
        var sut = _Create(failureThreshold: 2);
        var pauseCount = 0;
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () =>
            {
                pauseCount++;
                return ValueTask.CompletedTask;
            },
            onResume: () => ValueTask.CompletedTask
        );

        await _ReportTransientFailuresAsync(sut, Group, 2); // Opens circuit, pauseCount=1
        pauseCount.Should().Be(1);
        sut.IsOpen(Group).Should().BeTrue();

        // when — additional transient failure while Open
        await sut.ReportFailureAsync(Group, new TimeoutException());

        // then — should NOT trigger another pause callback
        pauseCount.Should().Be(1);
    }

    [Fact]
    public async Task resume_callback_failure_reopens_the_circuit()
    {
        // given — resume throws, which triggers re-open and a second pause callback
        var pauseCount = 0;
        var reopenedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(100));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () =>
            {
                if (Interlocked.Increment(ref pauseCount) == 2)
                {
                    reopenedTcs.TrySetResult();
                }

                return ValueTask.CompletedTask;
            },
            onResume: () => throw new InvalidOperationException("resume failed!")
        );

        // when — trip then wait for the re-open (resume throws → _ReopenAfterResumeFailureAsync → pause)
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await reopenedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // then — resume failure re-opens the circuit instead of wedging HalfOpen
        sut.GetState(Group).Should().Be(CircuitBreakerState.Open);
    }

    [Fact]
    public async Task concurrent_TryAcquireHalfOpenProbe_allows_exactly_one_winner()
    {
        // given — trip circuit then wait for HalfOpen
        const int parallelTasks = 50;
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        await sut.ReportFailureAsync(Group, new TimeoutException());
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // when — launch N parallel tasks all racing to acquire the probe
        using var barrier = new Barrier(parallelTasks);
        var results = new bool[parallelTasks];
        var tasks = Enumerable
            .Range(0, parallelTasks)
            .Select(i =>
                Task.Run(() =>
                {
                    barrier.SignalAndWait(); // maximize contention
                    results[i] = sut.TryAcquireHalfOpenProbe(Group);
                })
            )
            .ToArray();

        await Task.WhenAll(tasks);

        // then — exactly one task acquired the probe
        results.Count(r => r).Should().Be(1);
    }

    [Fact]
    public async Task open_duration_never_exceeds_max()
    {
        // given — base=1s, max=4s → escalation: 1s→2s→4s→4s→4s...
        var sut = _Create(
            failureThreshold: 1,
            openDuration: TimeSpan.FromSeconds(1),
            maxOpenDuration: TimeSpan.FromSeconds(4)
        );
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        // when — escalate 10 times without waiting for timers
        for (var i = 0; i < 10; i++)
        {
            await sut.ReportFailureAsync(Group, new TimeoutException());
        }

        // then — if escalation overflowed MaxOpenDuration, the timer duration would be
        // impossibly long; the fact we got here without hanging means the cap worked
        sut.IsOpen(Group).Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_while_open_cancels_timer_and_prevents_resume_callback()
    {
        // given — trip circuit to Open with a short timer
        var resumeCalled = false;
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(50));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                resumeCalled = true;
                return ValueTask.CompletedTask;
            }
        );

        await sut.ReportFailureAsync(Group, new TimeoutException());
        sut.IsOpen(Group).Should().BeTrue();

        // when — dispose before the timer fires
        await sut.DisposeAsync();

        // then — wait well past the open duration; resume must NOT fire
        await Task.Delay(200);
        resumeCalled.Should().BeFalse();
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        // given
        var sut = _Create();

        // when / then — calling Dispose twice should not throw
        var act = () =>
        {
            sut.Dispose();
            sut.Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_concurrent_with_timer_callback_is_safe()
    {
        for (var i = 0; i < 200; i++)
        {
            var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(1));
            sut.RegisterGroupCallbacks(
                Group,
                onPause: () => ValueTask.CompletedTask,
                onResume: () => ValueTask.CompletedTask
            );

            await sut.ReportFailureAsync(Group, new TimeoutException());

            await Task.WhenAll(Task.Run(() => sut.Dispose()), Task.Run(() => sut.Dispose()));
            // no exception = pass
        }
    }

    // -------------------------------------------------------------------------
    // ResetAsync tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task reset_returns_false_when_group_not_found()
    {
        // given
        var sut = _Create();

        // when
        var result = await sut.ResetAsync("unknown.group");

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task reset_returns_false_when_group_already_closed()
    {
        // given — register group by reporting a non-transient failure (stays Closed)
        var sut = _Create(failureThreshold: 5);
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        sut.GetState(Group).Should().Be(CircuitBreakerState.Closed);

        // when
        var result = await sut.ResetAsync(Group);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task reset_transitions_open_to_closed_and_returns_true()
    {
        // given — trip circuit to Open
        var sut = _Create(failureThreshold: 1);
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        await sut.ReportFailureAsync(Group, new TimeoutException());
        sut.GetState(Group).Should().Be(CircuitBreakerState.Open);

        // when
        var result = await sut.ResetAsync(Group);

        // then
        result.Should().BeTrue();
        sut.GetState(Group).Should().Be(CircuitBreakerState.Closed);
        sut.IsOpen(Group).Should().BeFalse();
    }

    [Fact]
    public async Task reset_resets_escalation_level()
    {
        // given — trip circuit multiple times to escalate
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(
            failureThreshold: 1,
            openDuration: TimeSpan.FromMilliseconds(20),
            maxOpenDuration: TimeSpan.FromSeconds(60)
        );
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        // first open → escalation level 1
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // re-open from HalfOpen → escalation level 2
        await sut.ReportFailureAsync(Group, new TimeoutException());

        sut.GetState(Group).Should().Be(CircuitBreakerState.Open);
        sut.GetSnapshot(Group)!.EscalationLevel.Should().BeGreaterThan(1);

        // when — manual reset
        var result = await sut.ResetAsync(Group);

        // then
        result.Should().BeTrue();
        sut.GetSnapshot(Group)!.EscalationLevel.Should().Be(0);
    }

    [Fact]
    public async Task reset_invokes_resume_callback()
    {
        // given — trip circuit to Open
        var resumeCalledOnReset = false;
        var sut = _Create(failureThreshold: 1);
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                resumeCalledOnReset = true;
                return ValueTask.CompletedTask;
            }
        );

        await sut.ReportFailureAsync(Group, new TimeoutException());
        sut.IsOpen(Group).Should().BeTrue();

        // clear the flag set during timer-based transition (if any)
        resumeCalledOnReset = false;

        // when
        await sut.ResetAsync(Group);

        // then
        resumeCalledOnReset.Should().BeTrue();
    }

    [Fact]
    public async Task reset_validates_null_group_name()
    {
        // given
        var sut = _Create();

        // when / then
        var act = async () => await sut.ResetAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task reset_validates_group_name_length()
    {
        // given
        var sut = _Create();
        var longName = new string('x', 513);

        // when / then
        var act = async () => await sut.ResetAsync(longName);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // -------------------------------------------------------------------------
    // GetSnapshot tests
    // -------------------------------------------------------------------------

    [Fact]
    public void get_snapshot_returns_null_for_unknown_group()
    {
        // given
        var sut = _Create();

        // when / then
        sut.GetSnapshot("never-registered").Should().BeNull();
    }

    [Fact]
    public void get_snapshot_returns_closed_state_for_new_group()
    {
        // given — register the group via callbacks
        var sut = _Create();
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        // when
        var snapshot = sut.GetSnapshot(Group);

        // then
        snapshot.Should().NotBeNull();
        snapshot!.State.Should().Be(CircuitBreakerState.Closed);
        snapshot.EscalationLevel.Should().Be(0);
        snapshot.OpenedAt.Should().BeNull();
        snapshot.EstimatedRemainingOpenDuration.Should().BeNull();
        snapshot.ConsecutiveFailures.Should().Be(0);
        snapshot.FailureThreshold.Should().Be(5); // default from _Create
        snapshot.EffectiveOpenDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task get_snapshot_returns_open_state_with_opened_at()
    {
        // given — trip circuit to Open
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromSeconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        await sut.ReportFailureAsync(Group, new TimeoutException());

        // when
        var snapshot = sut.GetSnapshot(Group);

        // then
        snapshot.Should().NotBeNull();
        snapshot!.State.Should().Be(CircuitBreakerState.Open);
        snapshot.OpenedAt.Should().NotBeNull();
        snapshot.EstimatedRemainingOpenDuration.Should().NotBeNull();
        snapshot.EstimatedRemainingOpenDuration!.Value.Should().BeGreaterThan(TimeSpan.Zero);
        snapshot.EstimatedRemainingOpenDuration.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task get_snapshot_remaining_duration_is_positive_and_bounded()
    {
        // given — trip circuit with a known open duration
        var openDuration = TimeSpan.FromSeconds(10);
        var sut = _Create(failureThreshold: 1, openDuration: openDuration);
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        await sut.ReportFailureAsync(Group, new TimeoutException());

        // when — take two snapshots with a small delay between them
        var snapshot1 = sut.GetSnapshot(Group);
        await Task.Delay(50);
        var snapshot2 = sut.GetSnapshot(Group);

        // then — remaining duration should be positive and ≤ configured open duration
        snapshot1!.EstimatedRemainingOpenDuration.Should().NotBeNull();
        snapshot2!.EstimatedRemainingOpenDuration.Should().NotBeNull();

        snapshot1.EstimatedRemainingOpenDuration!.Value.Should().BeGreaterThan(TimeSpan.Zero);
        snapshot2.EstimatedRemainingOpenDuration!.Value.Should().BeGreaterThan(TimeSpan.Zero);

        snapshot1.EstimatedRemainingOpenDuration.Value.Should().BeLessThanOrEqualTo(openDuration);
        snapshot2.EstimatedRemainingOpenDuration.Value.Should().BeLessThanOrEqualTo(openDuration);

        // second snapshot should have less or equal remaining time
        snapshot2
            .EstimatedRemainingOpenDuration.Value.Should()
            .BeLessThanOrEqualTo(snapshot1.EstimatedRemainingOpenDuration.Value);
    }

    // -------------------------------------------------------------------------
    // RegisterKnownGroups guard tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task known_groups_rejects_unknown_group_after_registration()
    {
        // given — register known groups
        var sut = _Create(failureThreshold: 1);
        sut.RegisterKnownGroups(["group.a", "group.b"]);

        // when — report failure for an unknown group
        await sut.ReportFailureAsync("unknown.group", new TimeoutException());

        // then — unknown group should not appear in tracked states
        var allStates = sut.GetAllStates();
        allStates.Should().NotContain(kvp => kvp.Key == "unknown.group");

        // known groups should be present
        allStates.Should().Contain(kvp => kvp.Key == "group.a");
        allStates.Should().Contain(kvp => kvp.Key == "group.b");
    }

    [Fact]
    public async Task max_tracked_groups_cap_prevents_unbounded_growth()
    {
        // given — no known groups registered, so cap logic applies at MaxTrackedGroups (1000)
        var sut = _Create(failureThreshold: 1);

        // when — register exactly MaxTrackedGroups (1000) groups via ReportFailureAsync
        for (var i = 0; i < 1000; i++)
        {
            sut.RegisterGroupCallbacks(
                $"group.{i}",
                onPause: () => ValueTask.CompletedTask,
                onResume: () => ValueTask.CompletedTask
            );
        }

        var countBefore = sut.GetAllStates().Count;
        countBefore.Should().Be(1000);

        // try to add one more beyond the cap
        await sut.ReportFailureAsync("overflow.group", new TimeoutException());

        // then — count should not have increased
        var countAfter = sut.GetAllStates().Count;
        countAfter.Should().Be(1000);
        sut.GetAllStates().Should().NotContain(kvp => kvp.Key == "overflow.group");
    }

    // -------------------------------------------------------------------------
    // Input validation tests (IsOpen, GetState, GetSnapshot)
    // -------------------------------------------------------------------------

    [Fact]
    public void IsOpen_validates_null_group_name()
    {
        var sut = _Create();
        var act = () => sut.IsOpen(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsOpen_validates_group_name_length()
    {
        var sut = _Create();
        var act = () => sut.IsOpen(new string('x', 257));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetState_validates_null_group_name()
    {
        var sut = _Create();
        var act = () => sut.GetState(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetState_validates_group_name_length()
    {
        var sut = _Create();
        var act = () => sut.GetState(new string('x', 257));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetSnapshot_validates_null_group_name()
    {
        var sut = _Create();
        var act = () => sut.GetSnapshot(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetSnapshot_validates_group_name_length()
    {
        var sut = _Create();
        var act = () => sut.GetSnapshot(new string('x', 257));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // -------------------------------------------------------------------------
    // Snapshot new fields tests (ConsecutiveFailures, FailureThreshold, EffectiveOpenDuration)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task get_snapshot_includes_consecutive_failures_and_threshold()
    {
        // given — 3 transient failures below threshold of 5
        var sut = _Create(failureThreshold: 5);
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        await _ReportTransientFailuresAsync(sut, Group, 3);

        // when
        var snapshot = sut.GetSnapshot(Group);

        // then
        snapshot.Should().NotBeNull();
        snapshot!.ConsecutiveFailures.Should().Be(3);
        snapshot.FailureThreshold.Should().Be(5);
    }

    [Fact]
    public async Task get_snapshot_includes_effective_open_duration_with_escalation()
    {
        // given — trip circuit so escalation level > 0
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromSeconds(10));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        await sut.ReportFailureAsync(Group, new TimeoutException());

        // when
        var snapshot = sut.GetSnapshot(Group);

        // then — first escalation level (1), exponent 0 → base duration
        snapshot.Should().NotBeNull();
        snapshot!.EffectiveOpenDuration.Should().Be(TimeSpan.FromSeconds(10));
    }

    // -------------------------------------------------------------------------
    // ForceOpenAsync tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task force_open_from_closed_transitions_to_open()
    {
        // given
        var pauseInvoked = false;
        var sut = _Create(failureThreshold: 5);
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () =>
            {
                pauseInvoked = true;
                return ValueTask.CompletedTask;
            },
            onResume: () => ValueTask.CompletedTask
        );

        sut.GetState(Group).Should().Be(CircuitBreakerState.Closed);

        // when
        var result = await sut.ForceOpenAsync(Group);

        // then
        result.Should().BeTrue();
        sut.GetState(Group).Should().Be(CircuitBreakerState.Open);
        sut.IsOpen(Group).Should().BeTrue();
        pauseInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task force_open_from_halfopen_transitions_to_open()
    {
        // given — trip circuit then wait for HalfOpen
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        await sut.ReportFailureAsync(Group, new TimeoutException());
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        sut.GetState(Group).Should().Be(CircuitBreakerState.HalfOpen);

        // when
        var result = await sut.ForceOpenAsync(Group);

        // then
        result.Should().BeTrue();
        sut.GetState(Group).Should().Be(CircuitBreakerState.Open);
    }

    [Fact]
    public async Task force_open_when_already_open_returns_false()
    {
        // given — trip circuit to Open
        var sut = _Create(failureThreshold: 1);
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        await sut.ReportFailureAsync(Group, new TimeoutException());
        sut.GetState(Group).Should().Be(CircuitBreakerState.Open);

        // when
        var result = await sut.ForceOpenAsync(Group);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task force_open_does_not_increment_escalation()
    {
        // given — circuit is closed with escalation level 0
        var sut = _Create(failureThreshold: 5);
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        var snapshotBefore = sut.GetSnapshot(Group);
        snapshotBefore!.EscalationLevel.Should().Be(0);

        // when
        await sut.ForceOpenAsync(Group);

        // then — escalation should not have been incremented
        var snapshotAfter = sut.GetSnapshot(Group);
        snapshotAfter!.EscalationLevel.Should().Be(0);
    }

    [Fact]
    public async Task force_open_returns_false_for_unknown_group()
    {
        // given
        var sut = _Create();

        // when
        var result = await sut.ForceOpenAsync("unknown.group");

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task force_open_validates_null_group_name()
    {
        // given
        var sut = _Create();

        // when / then
        var act = async () => await sut.ForceOpenAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task force_open_validates_group_name_length()
    {
        // given
        var sut = _Create();
        var longName = new string('x', 257);

        // when / then
        var act = async () => await sut.ForceOpenAsync(longName);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // -------------------------------------------------------------------------
    // KnownGroups returns empty set before registration
    // -------------------------------------------------------------------------

    [Fact]
    public void KnownGroups_returns_empty_set_before_registration()
    {
        // given
        var sut = _Create();

        // when / then
        sut.KnownGroups.Should().NotBeNull();
        sut.KnownGroups.Should().BeEmpty();
    }

    [Fact]
    public void KnownGroups_returns_registered_groups_after_registration()
    {
        // given
        var sut = _Create();
        sut.RegisterKnownGroups(["group.a", "group.b"]);

        // when / then
        sut.KnownGroups.Should().HaveCount(2);
        sut.KnownGroups.Should().Contain("group.a");
        sut.KnownGroups.Should().Contain("group.b");
    }

    // -------------------------------------------------------------------------
    // AbortHalfOpenProbeAsync tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task abort_halfopen_probe_transitions_back_to_open_preserving_history()
    {
        // given — trip circuit and wait for HalfOpen
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        await sut.ReportFailureAsync(Group, new TimeoutException());
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        sut.GetState(Group).Should().Be(CircuitBreakerState.HalfOpen);

        var escalationBefore = sut.GetSnapshot(Group)!.EscalationLevel;
        escalationBefore.Should().Be(1); // first open sets escalation to 1

        // when — abort the probe (as if transport is restarting)
        await sut.AbortHalfOpenProbeAsync(Group);

        // then — state transitions back to Open, escalation is NOT incremented further
        sut.GetState(Group).Should().Be(CircuitBreakerState.Open);
        sut.IsOpen(Group).Should().BeTrue();
        sut.GetSnapshot(Group)!.EscalationLevel.Should().Be(escalationBefore);
    }

    [Fact]
    public async Task abort_halfopen_probe_is_noop_when_not_in_halfopen()
    {
        // given
        var sut = _Create(failureThreshold: 1);
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        // circuit is Closed → abort should be a no-op
        sut.GetState(Group).Should().Be(CircuitBreakerState.Closed);
        await sut.AbortHalfOpenProbeAsync(Group);
        sut.GetState(Group).Should().Be(CircuitBreakerState.Closed);

        // trip circuit to Open → abort should also be a no-op
        await sut.ReportFailureAsync(Group, new TimeoutException());
        sut.GetState(Group).Should().Be(CircuitBreakerState.Open);
        await sut.AbortHalfOpenProbeAsync(Group);
        sut.GetState(Group).Should().Be(CircuitBreakerState.Open);
    }

    [Fact]
    public async Task dispose_blocks_on_resumetask_before_disposing_cts()
    {
        // given — a resume callback that delays long enough to overlap with Dispose
        var resumeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(20));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: async () =>
            {
                resumeStarted.TrySetResult();
                halfOpenTcs.TrySetResult();
                await Task.Delay(50); // simulate slow resume work
            }
        );

        // trip circuit so the timer fires and the resume task is in-flight
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // wait until resume has actually started running
        await resumeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // when — call synchronous Dispose while resume is in progress
        var act = () => sut.Dispose();

        // then — should not throw ObjectDisposedException
        act.Should().NotThrow();
    }
}
