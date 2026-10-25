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
        var handle = new FakeLeaseHandle(_timeProvider);
        handle.Enqueue(LeaseMonitor.LeaseState.Lost);
        await using var sut = _CreateMonitor(handle);

        // when
        sut.TriggerImmediateValidation();
        await DistributedLockTestSupport.WaitUntilAsync(() => sut.LostToken.IsCancellationRequested, AbortToken);

        // then
        sut.LostToken.IsCancellationRequested.Should().BeTrue();
        handle.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task should_not_cancel_when_unknown_is_followed_by_renewed_before_lease_duration()
    {
        // given
        var handle = new FakeLeaseHandle(_timeProvider);
        handle.Enqueue(new TimeoutException("transient"));
        handle.Enqueue(LeaseMonitor.LeaseState.Renewed);
        await using var sut = _CreateMonitor(handle);

        // when - Unknown then Renewed within budget — must NOT cancel. A real handle reports the renew it sent at 3s,
        // which moves the deadline from 9s to 3s + 10s - 1s = 12s.
        sut.TriggerImmediateValidation();
        await DistributedLockTestSupport.WaitUntilAsync(() => handle.InvocationCount == 1, AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        sut.OnLeaseRenewed(_timeProvider.GetTimestamp(), handle.LeaseDuration);
        sut.TriggerImmediateValidation();
        await DistributedLockTestSupport.WaitUntilAsync(() => handle.InvocationCount == 2, AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(6));
        sut.TriggerImmediateValidation();
        // The advance also fires the cadence wait armed at 3s, so the cadence and the nudge can each run a probe and
        // the count can pass 3 before it is read.
        await DistributedLockTestSupport.WaitUntilAsync(() => handle.InvocationCount >= 3, AbortToken);

        // then - still alive after Unknown → Renewed.
        sut.LostToken.IsCancellationRequested.Should().BeFalse();

        // and - convert the negative assertion into a positive observation: enqueue Lost and
        // confirm the monitor DOES react when storage subsequently confirms loss.
        handle.Enqueue(LeaseMonitor.LeaseState.Lost);
        sut.TriggerImmediateValidation();
        await DistributedLockTestSupport.WaitUntilAsync(() => sut.LostToken.IsCancellationRequested, AbortToken);
        sut.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_self_mark_lost_when_unknown_lifetime_reaches_lease_duration()
    {
        // given
        var handle = new FakeLeaseHandle(_timeProvider);
        handle.Enqueue(new TimeoutException("transient"));
        await using var sut = _CreateMonitor(handle);
        var lostSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var lostRegistration = sut.LostToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            lostSignal
        );

        // when
        sut.TriggerImmediateValidation();
        await DistributedLockTestSupport.WaitUntilAsync(() => handle.InvocationCount == 1, AbortToken);
        _timeProvider.Advance(handle.LeaseDuration);
        sut.TriggerImmediateValidation();
        await lostSignal.Task.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        sut.LostToken.IsCancellationRequested.Should().BeTrue();
        handle.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task should_validate_constructor_cadence_is_not_longer_than_lease()
    {
        // given
        var handle = new FakeLeaseHandle(_timeProvider)
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
        var handle = nullHandle ? null : new FakeLeaseHandle(_timeProvider);
        var timeProvider = nullTimeProvider ? null : _timeProvider;
        var logger = nullLogger ? null : LoggerFactory.CreateLogger(nameof(LeaseMonitor));
        var act = () => new LeaseMonitor(handle!, timeProvider!, logger!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task should_mark_lost_at_deadline_when_polling_keeps_returning_held()
    {
        // given - polling mode where storage confirms ownership on every probe. Held proves the key still exists
        // but does not extend it, so the store still expires the 10s lease at 10s. The deadline is
        // 10s - min(10% of 10s, 5s) = 9s.
        var handle = new FakeLeaseHandle(_timeProvider);
        await using var sut = _CreateMonitor(handle);

        // when - Held probes at 0s, 3s, 6s and just short of 9s
        await _RunIterationAndWait(sut, handle, expectedCount: 1);
        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        await _RunIterationAndWait(sut, handle, expectedCount: 2);
        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        await _RunIterationAndWait(sut, handle, expectedCount: 3);
        _timeProvider.Advance(TimeSpan.FromSeconds(3) - TimeSpan.FromMilliseconds(1));
        await _RunIterationAndWait(sut, handle, expectedCount: 4);

        // then
        sut.LostToken.IsCancellationRequested.Should().BeFalse();

        // when - the deadline, with the last probe still Held
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await _WaitForLossAsync(sut);

        // then - the deadline declared loss, not a probe
        handle.InvocationCount.Should().Be(4);
    }

    [Fact]
    public async Task should_signal_handle_lost_when_monitor_loop_faults()
    {
        // given - a logger that throws on Log calls. The first state transition logs via
        // LogLeaseMonitorStateChanged; the throw escapes _SetState → _RunIterationAsync →
        // _MonitoringLoopAsync, faulting MonitoringTask. The OnlyOnFaulted continuation MUST
        // cancel LostToken as a fail-safe.
        var handle = new FakeLeaseHandle(_timeProvider);
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
        await DistributedLockTestSupport.WaitUntilAsync(() => sut.LostToken.IsCancellationRequested, AbortToken);

        // then
        sut.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_dispose_idempotently()
    {
        // given
        await using var sut = _CreateMonitor(new FakeLeaseHandle(_timeProvider));

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
        // The lease is long enough that the loss deadline cannot end the loop inside the test's advances, so only
        // the dead weak reference can.
        var handle = new FakeLeaseHandle(_timeProvider)
        {
            LeaseDuration = TimeSpan.FromHours(1),
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
            await completed.Bounded();
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
        var handle = new BlockingLeaseHandle(_timeProvider.GetTimestamp(), blockSignal.Token);
        await using var sut = _CreateMonitor(handle);

        // Trigger an iteration that will block inside RenewOrValidateLeaseAsync.
        sut.TriggerImmediateValidation();
        await DistributedLockTestSupport.WaitUntilAsync(() => handle.IsBlocking, AbortToken);

        // when — the bound is what proves the point: the blocking call ends only on a cancellation, and the
        // handle's other cancellation source (blockSignal) is not released until this method returns, so a
        // disposal that did not propagate its token cannot complete at all and fails here by name. How many
        // milliseconds a completed disposal took is scheduler noise, so it is not asserted.
        await sut.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then — the blocking call observed the monitor's own token rather than the test's external abort.
        handle.ExitedBeforeRelease.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_mark_lost_before_deadline_when_held_streak_is_followed_by_single_unknown()
    {
        // given - a Held streak followed by one transient Unknown. Neither moves the deadline, and an Unknown alone
        // never declares loss, so the lease stays live until the 9s deadline and is lost exactly there.
        // A long cadence keeps the cadence timer out of the window; iterations run only when triggered.
        var handle = new FakeLeaseHandle(_timeProvider)
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

        await _RunIterationAndWait(sut, handle, expectedCount: 1);
        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        await _RunIterationAndWait(sut, handle, expectedCount: 2);
        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        await _RunIterationAndWait(sut, handle, expectedCount: 3);

        // when - the Unknown probe at 8s
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        await _RunIterationAndWait(sut, handle, expectedCount: 4);

        // then
        sut.LostToken.IsCancellationRequested.Should().BeFalse();

        // when
        _timeProvider.Advance(TimeSpan.FromSeconds(1));

        // then
        await _WaitForLossAsync(sut);
    }

    [Fact]
    public async Task should_keep_newer_deadline_when_older_renew_is_reported_after_newer_one()
    {
        // given - two renews: one sent at 2s with a 5s TTL, one sent at 4s with a 10s TTL. The store applied the
        // later send last, so the deadline is 4s + 10s - 1s = 13s. The older renew's 2s + 5s - 0.5s = 6.5s must not
        // replace it even when its success is reported last.
        var handle = new FakeLeaseHandle(_timeProvider) { MonitoringCadence = TimeSpan.FromSeconds(10) };
        await using var sut = _CreateMonitor(handle);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        var olderSend = _timeProvider.GetTimestamp();
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        var newerSend = _timeProvider.GetTimestamp();

        // when
        sut.OnLeaseRenewed(newerSend, TimeSpan.FromSeconds(10));
        sut.OnLeaseRenewed(olderSend, TimeSpan.FromSeconds(5));
        _timeProvider.Advance(TimeSpan.FromSeconds(9) - TimeSpan.FromMilliseconds(1));
        await DistributedLockTestSupport.DrainUntilAsync(() => sut.LostToken.IsCancellationRequested, AbortToken);

        // then
        sut.LostToken.IsCancellationRequested.Should().BeFalse();

        // when
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));

        // then
        await _WaitForLossAsync(sut);
    }

    [Fact]
    public async Task should_not_mark_lost_when_renew_made_lease_non_expiring()
    {
        // given - a write lease renewed with an infinite TTL no longer expires in the store
        var handle = new FakeLeaseHandle(_timeProvider) { MonitoringCadence = TimeSpan.FromSeconds(10) };
        await using var sut = _CreateMonitor(handle);
        _timeProvider.Advance(TimeSpan.FromSeconds(1));

        // when
        sut.OnLeaseRenewed(_timeProvider.GetTimestamp(), Timeout.InfiniteTimeSpan);
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await DistributedLockTestSupport.DrainUntilAsync(() => sut.LostToken.IsCancellationRequested, AbortToken);

        // then
        sut.LostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_mark_lost_when_queued_deadline_callback_runs_after_renew_made_lease_non_expiring()
    {
        // given - the deadline callback was already queued when a renew made the lease non-expiring
        var timeProvider = new CallbackCapturingTimeProvider();
        var handle = new FakeLeaseHandle(timeProvider) { MonitoringCadence = TimeSpan.FromSeconds(10) };
        await using var sut = new LeaseMonitor(handle, timeProvider, LoggerFactory.CreateLogger(nameof(LeaseMonitor)));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        sut.OnLeaseRenewed(timeProvider.GetTimestamp(), Timeout.InfiniteTimeSpan);

        // when
        timeProvider.RunCallbacks("DeadlineTimerState");
        await DistributedLockTestSupport.DrainUntilAsync(() => sut.LostToken.IsCancellationRequested, AbortToken);

        // then
        sut.LostToken.IsCancellationRequested.Should().BeFalse();
    }

    private static async Task _WaitForLossAsync(LeaseMonitor sut)
    {
        var lostSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = sut.LostToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            lostSignal
        );
        await lostSignal.Task.Bounded();
        sut.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    private static async Task _RunIterationAndWait(LeaseMonitor sut, FakeLeaseHandle handle, int expectedCount)
    {
        sut.TriggerImmediateValidation();
        await DistributedLockTestSupport.WaitUntilAsync(() => handle.InvocationCount >= expectedCount, AbortToken);
    }

    private LeaseMonitor _CreateMonitor(FakeLeaseHandle handle)
    {
        return new LeaseMonitor(handle, _timeProvider, LoggerFactory.CreateLogger(nameof(LeaseMonitor)));
    }

    private LeaseMonitor _CreateMonitor(LeaseMonitor.ILeaseHandle handle)
    {
        return new LeaseMonitor(handle, _timeProvider, LoggerFactory.CreateLogger(nameof(LeaseMonitor)));
    }

    private sealed class BlockingLeaseHandle(long acquireTimestamp, CancellationToken externalAbort)
        : LeaseMonitor.ILeaseHandle
    {
        public string Resource => "blocking-resource";
        public string LeaseId => "blocking-lock";
        public TimeSpan LeaseDuration => TimeSpan.FromSeconds(10);
        public TimeSpan MonitoringCadence => TimeSpan.FromSeconds(5);
        public long AcquireTimestamp => acquireTimestamp;
        public TimeSpan StorageDeadline => TimeSpan.FromSeconds(5);
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
        var handle = new FakeLeaseHandle(_timeProvider);
        handle.Enqueue(LeaseMonitor.LeaseState.Lost);
        await using var sut = _CreateMonitor(handle);

        var callbackInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.LostToken.Register(() =>
        {
            callbackInvoked.TrySetResult();
            sut.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
}
