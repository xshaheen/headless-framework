// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tests.Fakes;

namespace Tests.RegularLocks;

public sealed class LeaseMonitorTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task should_cancel_handle_lost_token_when_handle_returns_lost()
    {
        // given
        var handle = new FakeLeaseHandle();
        handle.Enqueue(LeaseMonitor.LeaseState.Lost);
        await using var sut = _CreateMonitor(handle);

        // when
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => sut.LostToken.IsCancellationRequested, AbortToken);

        // then
        sut.LostToken.IsCancellationRequested.Should().BeTrue();
        handle.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task should_not_cancel_when_unknown_is_followed_by_renewed_before_lease_duration()
    {
        // given
        var handle = new FakeLeaseHandle();
        handle.Enqueue(new TimeoutException("transient"));
        handle.Enqueue(LeaseMonitor.LeaseState.Renewed);
        await using var sut = _CreateMonitor(handle);

        // when - Unknown then Renewed within budget — must NOT cancel.
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => handle.InvocationCount == 1, AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => handle.InvocationCount == 2, AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(6));
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => handle.InvocationCount == 3, AbortToken);

        // then - still alive after Unknown → Renewed.
        sut.LostToken.IsCancellationRequested.Should().BeFalse();

        // and - convert the negative assertion into a positive observation: enqueue Lost and
        // confirm the monitor DOES react when storage subsequently confirms loss.
        handle.Enqueue(LeaseMonitor.LeaseState.Lost);
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => sut.LostToken.IsCancellationRequested, AbortToken);
        sut.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_self_mark_lost_when_unknown_lifetime_reaches_lease_duration()
    {
        // given
        var handle = new FakeLeaseHandle();
        handle.Enqueue(new TimeoutException("transient"));
        await using var sut = _CreateMonitor(handle);

        // when
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => handle.InvocationCount == 1, AbortToken);
        _timeProvider.Advance(handle.LeaseDuration);
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => sut.LostToken.IsCancellationRequested, AbortToken);

        // then
        sut.LostToken.IsCancellationRequested.Should().BeTrue();
        handle.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task should_validate_constructor_cadence_is_not_longer_than_lease()
    {
        // given
        var handle = new FakeLeaseHandle
        {
            LeaseDuration = TimeSpan.FromSeconds(5),
            MonitoringCadence = TimeSpan.FromSeconds(6),
        };

        // when
        var act = () => _CreateMonitor(handle);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void should_throw_argument_null_exception_on_constructor_null_parameters(
        bool nullHandle,
        bool nullTimeProvider,
        bool nullLogger
    )
    {
        var handle = nullHandle ? null : new FakeLeaseHandle();
        var timeProvider = nullTimeProvider ? null : _timeProvider;
        var logger = nullLogger ? null : LoggerFactory.CreateLogger(nameof(LeaseMonitor));
        var act = () => new LeaseMonitor(handle!, timeProvider!, logger!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task should_not_self_mark_lost_when_polling_returns_held_past_lease_window()
    {
        // given - polling mode (no auto-extend) where every iteration returns Held.
        // The safety net "leaseLifetime >= _leaseDuration => Lost" must only fire from Unknown,
        // not from Held returned by storage confirming continued ownership.
        var handle = new FakeLeaseHandle();
        for (var i = 0; i < 7; i++)
        {
            handle.Enqueue(LeaseMonitor.LeaseState.Held);
        }

        await using var sut = _CreateMonitor(handle);

        // when - drive multiple cadence iterations past 2x lease duration (10s) with Held returns.
        for (var i = 0; i < 6; i++)
        {
            sut.TriggerImmediateValidation();
            await _DrainUntilAsync(() => handle.InvocationCount >= i + 1, AbortToken);
            _timeProvider.Advance(TimeSpan.FromSeconds(5));
        }

        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => handle.InvocationCount >= 7, AbortToken);

        // then - storage repeatedly confirmed ownership; monitor must not declare Lost.
        sut.LostToken.IsCancellationRequested.Should().BeFalse();

        // and - positive observation: when storage subsequently confirms loss, the monitor DOES react.
        handle.Enqueue(LeaseMonitor.LeaseState.Lost);
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => sut.LostToken.IsCancellationRequested, AbortToken);
        sut.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_signal_handle_lost_when_monitor_loop_faults()
    {
        // given - a logger that throws on Log calls. The first state transition logs via
        // LogLeaseMonitorStateChanged; the throw escapes _SetState → _RunIterationAsync →
        // _MonitoringLoopAsync, faulting MonitoringTask. The OnlyOnFaulted continuation MUST
        // cancel LostToken as a fail-safe.
        var handle = new FakeLeaseHandle();
        handle.Enqueue(LeaseMonitor.LeaseState.Renewed);
        var logger = Substitute.For<ILogger>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        logger
            .When(l =>
                l.Log(
                    Arg.Any<LogLevel>(),
                    Arg.Any<EventId>(),
                    Arg.Any<object>(),
                    Arg.Any<Exception?>(),
                    Arg.Any<Func<object, Exception?, string>>()
                )
            )
            .Do(_ => throw new InvalidOperationException("logger boom"));
        await using var sut = new LeaseMonitor(handle, _timeProvider, logger);

        // when
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => sut.LostToken.IsCancellationRequested, AbortToken);

        // then
        sut.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_dispose_idempotently()
    {
        // given
        await using var sut = _CreateMonitor(new FakeLeaseHandle());

        // when
        await sut.DisposeAsync();
        await sut.DisposeAsync();

        // then
        sut.MonitoringTask.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task should_exit_monitoring_loop_when_only_reference_is_garbage_collected()
    {
        // AC6 — GC abandonment. If a consumer drops the monitor without disposing it, the
        // loop must observe a dead WeakReference<LeaseMonitor> on its next cadence iteration
        // and exit. The OnlyOnFaulted continuation MUST NOT capture `this` (otherwise it would
        // strong-root the monitor for the lifetime of MonitoringTask, defeating this invariant).
        var handle = new FakeLeaseHandle
        {
            LeaseDuration = TimeSpan.FromMinutes(1),
            MonitoringCadence = TimeSpan.FromSeconds(1),
        };
        var monitoringTask = _CreateMonitorAndDropReference(handle);

        // when - drive GC + a cadence advance repeatedly until the loop observes the dead
        // WeakReference and exits. Two non-determinisms make a single pass flaky: (1) GC need not
        // reclaim an object parked behind an in-flight async continuation on the first collection,
        // and (2) FakeTimeProvider.Advance only fires timers already registered at advance time,
        // so an advance can race the loop re-parking on its next cadence wait and be silently lost.
        // Retrying both — bounded — is deterministic without depending on collection or scheduler
        // timing. In production real time always advances, so neither race exists.
        for (var attempt = 0; attempt < 50 && !monitoringTask.IsCompleted; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);

            _timeProvider.Advance(TimeSpan.FromSeconds(2));

            // Yield real wall-clock time so the loop's continuation can resume on the thread pool
            // and re-check the WeakReference before the next attempt.
            var completed = await Task.WhenAny(monitoringTask, Task.Delay(TimeSpan.FromMilliseconds(100), AbortToken));
            await completed;
        }

        // then
        monitoringTask.IsCompleted.Should().BeTrue();
    }

    private Task _CreateMonitorAndDropReference(FakeLeaseHandle handle)
    {
        // Helper isolates the strong-reference scope so the JIT cannot keep the LeaseMonitor
        // alive in a local on the caller's stack frame across GC.Collect.
        // CA2000: intentionally not disposed — this test verifies GC reclaims an abandoned monitor.
#pragma warning disable CA2000 // CA2000: intentional — this test verifies GC reclaims an abandoned (undisposed) monitor; disposing defeats it.
        var monitor = new LeaseMonitor(handle, _timeProvider, LoggerFactory.CreateLogger(nameof(LeaseMonitor)));
#pragma warning restore CA2000
        var task = monitor.MonitoringTask;
        monitor = null;
        _ = monitor; // suppress "unused" — intent is to overwrite the local.
        return task;
    }

    [Fact]
    public async Task should_complete_dispose_within_bounded_time_when_handle_call_blocks()
    {
        // AC7 — disposal timing. A blocking RenewOrValidateLeaseAsync must not strand
        // DisposeAsync; the loop's cancellation token must propagate so the blocking call
        // unblocks promptly.
        using var blockSignal = new CancellationTokenSource();
        var handle = new BlockingLeaseHandle(blockSignal.Token);
        await using var sut = _CreateMonitor(handle);

        // Trigger an iteration that will block inside RenewOrValidateLeaseAsync.
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => handle.IsBlocking, AbortToken);

        // when
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await sut.DisposeAsync();
        stopwatch.Stop();

        // then - generous 500ms bound. Spec aims for ~100ms but CI variability matters; this
        // budget catches an actual deadlock or unbounded wait while remaining stable in CI.
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500);
        handle.ExitedBeforeRelease.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_self_mark_lost_when_held_streak_followed_by_single_unknown()
    {
        // Regression for the "Held resets leaseTimestamp too" fix (issue #6). Without it, a
        // long Held streak in polling mode would let the safety-net elapsed window grow even
        // though storage repeatedly confirmed ownership. After enough Held probes, a single
        // transient Unknown would then trip the safety net on the FOLLOWING iteration — a
        // false-positive Lost when ownership was never actually in question.
        //
        // With the fix, Held resets leaseTimestamp on every probe, so the elapsed window
        // starts fresh after each confirmed-ownership signal. A single subsequent Unknown
        // cannot accumulate enough elapsed time to trip the safety net.
        //
        // Use a long cadence so the cadence timer never fires inside the test window — we
        // drive iterations only via TriggerImmediateValidation to keep the sequence deterministic.
        var handle = new FakeLeaseHandle
        {
            LeaseDuration = TimeSpan.FromSeconds(10),
            MonitoringCadence = TimeSpan.FromSeconds(10),
        };
        for (var i = 0; i < 3; i++)
        {
            handle.Enqueue(LeaseMonitor.LeaseState.Held);
        }
        handle.Enqueue(new TimeoutException("transient"));

        await using var sut = _CreateMonitor(handle);

        // Drive three Held probes, advancing the clock so each carries a leaseLifetime that
        // would individually exceed the lease window if the prior Held had not reset it. The
        // advance happens AFTER the iteration was triggered and observed, but BEFORE we
        // probe the safety-net check on the next iteration.
        await _RunIterationAndWait(sut, handle, expectedCount: 1);
        _timeProvider.Advance(TimeSpan.FromSeconds(9));
        await _RunIterationAndWait(sut, handle, expectedCount: 2);
        _timeProvider.Advance(TimeSpan.FromSeconds(9));
        await _RunIterationAndWait(sut, handle, expectedCount: 3);

        // Modest advance, then the Unknown probe. Total elapsed since the LAST Held reset is
        // small, so the safety net should NOT fire.
        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        await _RunIterationAndWait(sut, handle, expectedCount: 4);

        sut.LostToken.IsCancellationRequested.Should().BeFalse();
    }

    private static async Task _RunIterationAndWait(LeaseMonitor sut, FakeLeaseHandle handle, int expectedCount)
    {
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => handle.InvocationCount >= expectedCount, AbortToken);
    }

    private LeaseMonitor _CreateMonitor(FakeLeaseHandle handle)
    {
        return new LeaseMonitor(handle, _timeProvider, LoggerFactory.CreateLogger(nameof(LeaseMonitor)));
    }

    private LeaseMonitor _CreateMonitor(LeaseMonitor.ILeaseHandle handle)
    {
        return new LeaseMonitor(handle, _timeProvider, LoggerFactory.CreateLogger(nameof(LeaseMonitor)));
    }

    private sealed class BlockingLeaseHandle(CancellationToken externalAbort) : LeaseMonitor.ILeaseHandle
    {
        public string Resource => "blocking-resource";
        public string LeaseId => "blocking-lock";
        public TimeSpan LeaseDuration => TimeSpan.FromSeconds(10);
        public TimeSpan MonitoringCadence => TimeSpan.FromSeconds(5);
        public bool IsBlocking { get; private set; }
        public bool ExitedBeforeRelease { get; private set; }

        public async Task<LeaseMonitor.LeaseState> RenewOrValidateLeaseAsync(CancellationToken cancellationToken)
        {
            IsBlocking = true;

            try
            {
                // Block until the disposal token (passed by the monitor loop) cancels.
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, externalAbort);
                await Task.Delay(Timeout.Infinite, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ExitedBeforeRelease = true;
                throw;
            }

            return LeaseMonitor.LeaseState.Held;
        }
    }

    [Fact]
    public async Task should_not_deadlock_when_callback_disposes_monitor()
    {
        // given
        var handle = new FakeLeaseHandle();
        handle.Enqueue(LeaseMonitor.LeaseState.Lost);
        await using var sut = _CreateMonitor(handle);

        var callbackInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.LostToken.Register(() =>
        {
            callbackInvoked.TrySetResult();
#pragma warning disable MA0045 // CancellationToken.Register callbacks are synchronous; sync dispose is the behavior under test.
            sut.DisposeAsync().AsTask().GetAwaiter().GetResult();
#pragma warning restore MA0045
            callbackCompleted.TrySetResult();
        });

        // when
        sut.TriggerImmediateValidation();

        // Wait for the callback to be invoked. Generous timeout so a starved continuation under
        // heavy parallel load fails the assertion below rather than timing out spuriously here.
        await callbackInvoked.Task.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        // Synchronous callback disposal should complete without hanging/deadlocking.
        await callbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
        sut.MonitoringTask.IsCompleted.Should().BeTrue();
    }

    private static async Task _DrainUntilAsync(Func<bool> condition, CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < 2000 && !condition(); i++)
        {
            if (i % 100 == 0)
            {
                await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
            }
            else
            {
                await Task.Yield();
            }
        }
    }
}
