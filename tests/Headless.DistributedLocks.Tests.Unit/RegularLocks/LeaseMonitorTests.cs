// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
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

        // when
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => handle.InvocationCount == 1);
        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => handle.InvocationCount == 2);
        _timeProvider.Advance(TimeSpan.FromSeconds(6));
        sut.TriggerImmediateValidation();
        await _DrainUntilAsync(() => handle.InvocationCount == 3);

        // then
        sut.HandleLostToken.IsCancellationRequested.Should().BeFalse();
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
