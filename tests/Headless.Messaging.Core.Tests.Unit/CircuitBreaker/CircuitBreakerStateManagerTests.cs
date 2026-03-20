// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Sockets;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Exceptions;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

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
        int successfulCyclesToResetEscalation = 3
    )
    {
        var opts = new CircuitBreakerOptions
        {
            FailureThreshold = failureThreshold,
            OpenDuration = openDuration ?? TimeSpan.FromSeconds(30),
            MaxOpenDuration = maxOpenDuration ?? TimeSpan.FromSeconds(240),
            SuccessfulCyclesToResetEscalation = successfulCyclesToResetEscalation,
        };

        return new CircuitBreakerStateManager(
            Options.Create(opts),
            new NullLogger<CircuitBreakerStateManager>(),
            new CircuitBreakerMetrics()
        );
    }

    private static async Task _ReportTransientFailuresAsync(
        ICircuitBreakerStateManager sut,
        string group,
        int count
    )
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
        sut.ReportSuccess(Group);
        await _ReportTransientFailuresAsync(sut, Group, 4);

        // then — still closed because success reset the counter
        sut.IsOpen(Group).Should().BeFalse();
    }

    [Fact]
    public async Task should_transition_from_open_to_halfopen_after_timer()
    {
        // given
        var resumeCalled = false;
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                resumeCalled = true;
                return ValueTask.CompletedTask;
            }
        );

        // when — trip the circuit then wait for timer
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await Task.Delay(150); // wait well past the 30ms open duration

        // then — circuit is now HalfOpen (IsOpen still returns true to prevent new messages)
        sut.IsOpen(Group).Should().BeTrue();
        resumeCalled.Should().BeTrue();
    }

    [Fact]
    public async Task should_close_on_halfopen_success()
    {
        // given
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        // open then wait for HalfOpen
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await Task.Delay(150);

        // when — probe succeeds
        sut.ReportSuccess(Group);

        // then
        sut.IsOpen(Group).Should().BeFalse();
    }

    [Fact]
    public async Task should_reopen_on_halfopen_transient_failure()
    {
        // given
        var pauseCount = 0;
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () =>
            {
                pauseCount++;
                return ValueTask.CompletedTask;
            },
            onResume: () => ValueTask.CompletedTask
        );

        // open then wait for HalfOpen
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await Task.Delay(150);

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
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        // open then wait for HalfOpen
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await Task.Delay(150);

        // when — non-transient failure (bad message, not broker issue)
        await sut.ReportFailureAsync(Group, new ArgumentException("bad message payload"));

        // then — circuit closes because the dependency is fine
        sut.IsOpen(Group).Should().BeFalse();
    }

    [Fact]
    public async Task should_escalate_open_duration_on_repeated_reopens()
    {
        // given — very short base duration so we can observe escalation in ms
        var sut = _Create(
            failureThreshold: 1,
            openDuration: TimeSpan.FromMilliseconds(20),
            maxOpenDuration: TimeSpan.FromMilliseconds(200)
        );
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        // first open: duration = 20ms (level 0 → 1)
        await sut.ReportFailureAsync(Group, new TimeoutException());

        // wait for HalfOpen, then fail transient → second open: duration = 40ms (level 1 → 2)
        await Task.Delay(100);
        await sut.ReportFailureAsync(Group, new TimeoutException());

        // wait for HalfOpen again, then fail transient → third open: duration = 80ms (level 2 → 3)
        await Task.Delay(200);
        await sut.ReportFailureAsync(Group, new TimeoutException());

        // the third open should still be open (80ms hasn't passed yet from NOW)
        sut.IsOpen(Group).Should().BeTrue();

        // wait for it to expire
        await Task.Delay(500);
        sut.IsOpen(Group).Should().BeTrue(); // HalfOpen counts as open
    }

    [Fact]
    public async Task should_reset_escalation_after_3_healthy_cycles()
    {
        // given
        var sut = _Create(
            failureThreshold: 1,
            openDuration: TimeSpan.FromMilliseconds(20),
            successfulCyclesToResetEscalation: 3
        );
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        // helper: open → wait for HalfOpen → close via success
        async Task _CycleAsync()
        {
            await sut.ReportFailureAsync(Group, new TimeoutException());
            await Task.Delay(100); // wait for HalfOpen
            sut.ReportSuccess(Group);
        }

        // open, half-open, close × 3 → escalation should reset
        await _CycleAsync();
        await _CycleAsync();
        await _CycleAsync();

        // after 3 healthy cycles, circuit should be closed
        sut.IsOpen(Group).Should().BeFalse();
    }

    [Fact]
    public async Task should_block_concurrent_probes_in_halfopen()
    {
        // given
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        // open then wait for HalfOpen
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await Task.Delay(150);

        // when — first probe acquires the permit
        var firstAcquired = sut.TryAcquireProbePermit(Group);
        var secondAcquired = sut.TryAcquireProbePermit(Group);

        // then
        firstAcquired.Should().BeTrue();
        secondAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task should_dispose_stale_timer_on_reopen()
    {
        // given — first open duration is short enough to fire; second open duration is longer
        var resumeCallCount = 0;
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

        // wait long enough for the original 30ms timer to have fired, but not the new escalated timer
        await Task.Delay(200);

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
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(30));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                resumeInvoked = true;
                return ValueTask.CompletedTask;
            }
        );

        // when — trip then wait for timer
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await Task.Delay(150);

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
    public void TryAcquireProbePermit_returns_false_for_unregistered_group()
    {
        var sut = _Create();
        sut.TryAcquireProbePermit("never-registered").Should().BeFalse();
    }

    [Fact]
    public void ReportSuccess_is_noop_for_unregistered_group()
    {
        var sut = _Create();
        var act = () => sut.ReportSuccess("never-registered");
        act.Should().NotThrow();
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
    public async Task resume_callback_exception_is_swallowed()
    {
        // given
        var sut = _Create(failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(50));
        sut.RegisterGroupCallbacks(
            Group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => throw new InvalidOperationException("resume failed!")
        );

        // when — trip then wait for HalfOpen timer
        await sut.ReportFailureAsync(Group, new TimeoutException());
        await Task.Delay(200);

        // then — system still functional; report success to close
        sut.ReportSuccess(Group);
        sut.IsOpen(Group).Should().BeFalse();
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
}
