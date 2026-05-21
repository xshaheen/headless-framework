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
        await _DrainUntilAsync(() => sut.HandleLostToken.IsCancellationRequested);

        // then
        sut.HandleLostToken.IsCancellationRequested.Should().BeTrue();
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
        await _DrainUntilAsync(() => handle.InvocationCount == 1);
        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => handle.InvocationCount == 2);
        _timeProvider.Advance(TimeSpan.FromSeconds(6));
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => handle.InvocationCount == 3);

        // then - still alive after Unknown → Renewed.
        sut.HandleLostToken.IsCancellationRequested.Should().BeFalse();

        // and - convert the negative assertion into a positive observation: enqueue Lost and
        // confirm the monitor DOES react when storage subsequently confirms loss.
        handle.Enqueue(LeaseMonitor.LeaseState.Lost);
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => sut.HandleLostToken.IsCancellationRequested);
        sut.HandleLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_self_mark_lost_when_unknown_lifetime_exceeds_lease_duration()
    {
        // given
        var handle = new FakeLeaseHandle();
        handle.Enqueue(new TimeoutException("transient"));
        await using var sut = _CreateMonitor(handle);

        // when
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => handle.InvocationCount == 1);
        _timeProvider.Advance(TimeSpan.FromSeconds(11));
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => sut.HandleLostToken.IsCancellationRequested);

        // then
        sut.HandleLostToken.IsCancellationRequested.Should().BeTrue();
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

    [Fact]
    public async Task should_not_self_mark_lost_when_polling_returns_held_past_lease_window()
    {
        // given - polling mode (no auto-extend) where every iteration returns Held.
        // The safety net "leaseLifetime > _leaseDuration => Lost" must only fire from Unknown,
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
            await _DrainUntilAsync(() => handle.InvocationCount >= i + 1);
            _timeProvider.Advance(TimeSpan.FromSeconds(5));
        }

        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => handle.InvocationCount >= 7);

        // then - storage repeatedly confirmed ownership; monitor must not declare Lost.
        sut.HandleLostToken.IsCancellationRequested.Should().BeFalse();

        // and - positive observation: when storage subsequently confirms loss, the monitor DOES react.
        handle.Enqueue(LeaseMonitor.LeaseState.Lost);
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => sut.HandleLostToken.IsCancellationRequested);
        sut.HandleLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_signal_handle_lost_when_monitor_loop_faults()
    {
        // given - a logger that throws on Log calls. The first state transition logs via
        // LogLeaseMonitorStateChanged; the throw escapes _SetState → _RunIterationAsync →
        // _MonitoringLoopAsync, faulting MonitoringTask. The OnlyOnFaulted continuation MUST
        // cancel HandleLostToken as a fail-safe.
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
        await _DrainUntilAsync(() => sut.HandleLostToken.IsCancellationRequested);

        // then
        sut.HandleLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_dispose_idempotently()
    {
        // given
        var sut = _CreateMonitor(new FakeLeaseHandle());

        // when
        await sut.DisposeAsync();
        await sut.DisposeAsync();

        // then
        sut.MonitoringTask.IsCompleted.Should().BeTrue();
    }

    private LeaseMonitor _CreateMonitor(FakeLeaseHandle handle)
    {
        return new LeaseMonitor(handle, _timeProvider, LoggerFactory.CreateLogger(nameof(LeaseMonitor)));
    }

    private static async Task _DrainUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 2000 && !condition(); i++)
        {
            if (i % 100 == 0)
            {
                await Task.Delay(1);
            }
            else
            {
                await Task.Yield();
            }
        }
    }
}
