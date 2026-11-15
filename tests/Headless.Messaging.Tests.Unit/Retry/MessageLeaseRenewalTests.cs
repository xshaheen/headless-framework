// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Tests.Helpers;

namespace Tests.Retry;

/// <summary>
/// The renewal loop cancels an attempt only when its lease is gone or could lapse, never on one store blip.
/// </summary>
public sealed class MessageLeaseRenewalTests : TestBase
{
    private static readonly TimeSpan _Lease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _Interval = TimeSpan.FromSeconds(10);

    private readonly TimerArmedSignalClock _clock = new(DateTimeOffset.UnixEpoch);
    private readonly FakeTimeProvider _backoffClock = new();
    private readonly ConcurrentQueue<Func<bool>> _outcomes = new();
    private Func<CancellationToken, Task<bool>>? _hungRenewal;
    private int _calls;

    [Fact]
    public async Task should_keep_the_attempt_running_when_one_renewal_throws_and_the_next_succeeds()
    {
        // given
        _Then(() => throw new TimeoutException("store blip"), () => true, () => true);
        await using var renewal = _Start();

        // when
        await _TickAsync(3);

        // then
        renewal.IsLost.Should().BeFalse();
        renewal.AttemptToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_cancel_the_attempt_once_failures_could_let_the_confirmed_lease_lapse()
    {
        // given: at the default third-of-lease cadence, the second failure in a row leaves no tick before the lease ends
        _Then(() => throw new TimeoutException("store down"), () => throw new TimeoutException("store down"));
        await using var renewal = _Start();

        // when
        await _TickAsync(1);
        renewal.IsLost.Should().BeFalse("the lease confirmed at start still has two intervals left");
        await _TickAsync(1);

        // then
        renewal.IsLost.Should().BeTrue();
        renewal.AttemptToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_measure_the_lapse_from_the_last_confirmed_renewal()
    {
        // given: fail, confirm, fail; the success restarts the lease, so the second failure is again the first in a row
        _Then(() => throw new TimeoutException(), () => true, () => throw new TimeoutException(), () => true);
        await using var renewal = _Start();

        // when
        await _TickAsync(4);

        // then
        renewal.IsLost.Should().BeFalse();
    }

    [Fact]
    public async Task should_cancel_on_the_first_failure_when_the_next_tick_would_land_after_the_lease()
    {
        // given: an interval close to the lease leaves no second chance
        _Then(() => throw new TimeoutException());
        await using var renewal = _Start(interval: TimeSpan.FromSeconds(20));

        // when
        await _TickAsync(1, TimeSpan.FromSeconds(20));

        // then
        renewal.IsLost.Should().BeTrue();
    }

    [Fact]
    public async Task should_cancel_at_once_when_the_lease_is_gone()
    {
        // given: the fence matched no row, so another node may own it
        _Then(() => false);
        await using var renewal = _Start();

        // when
        await _TickAsync(1);

        // then
        renewal.IsLost.Should().BeTrue();
        renewal.AttemptToken.IsCancellationRequested.Should().BeTrue();
        var act = renewal.ThrowIfLost;
        act.Should().Throw<MessageLeaseLostException>();
    }

    [Fact]
    public async Task should_stop_renewing_and_keep_the_attempt_when_the_store_cannot_renew()
    {
        // given
        _Then(() => throw new NotSupportedException());
        await using var renewal = _Start();

        // when
        await _TickAsync(1);
        _clock.Advance(_Interval * 3);

        // then
        renewal.IsLost.Should().BeFalse();
        Volatile.Read(ref _calls).Should().Be(1);
    }

    [Fact]
    public async Task should_time_out_a_hung_renewal_when_the_backoff_clock_passes_the_interval()
    {
        // given: a store call that answers only when its timeout cancels it
        var renewalStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewalTimedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _hungRenewal = async token =>
        {
            renewalStarted.TrySetResult();
            var hung = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var registration = token.Register(() =>
            {
                renewalTimedOut.TrySetResult();
                hung.TrySetCanceled(token);
            });

            return await hung.Task.Bounded();
        };
        await using var renewal = _Start();
        _clock.Advance(_Interval);
        await renewalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when: the app clock already passed the interval, so only the back-off clock can end the call
        renewalTimedOut.Task.IsCompleted.Should().BeFalse();
        _backoffClock.Advance(_Interval);

        // then
        await renewalTimedOut.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    private void _Then(params Func<bool>[] outcomes)
    {
        foreach (var outcome in outcomes)
        {
            _outcomes.Enqueue(outcome);
        }
    }

    private MessageLeaseRenewal _Start(TimeSpan? interval = null)
    {
        var policy = new RetryPolicyOptions { DispatchTimeout = _Lease, LeaseRenewalInterval = interval ?? _Interval };

        return MessageLeaseRenewal.Start(
            new MediumMessage
            {
                StorageId = Guid.NewGuid(),
                Origin = new Message(new Dictionary<string, string?>(StringComparer.Ordinal), "{}"),
                Content = "{}",
                Lane = MessageLane.Bus,
            },
            (_, _, token) =>
            {
                Interlocked.Increment(ref _calls);
                if (_hungRenewal is { } hung)
                {
                    return new ValueTask<bool>(hung(token));
                }

                // Once the scripted outcomes run out, every renewal confirms the lease.
                return ValueTask.FromResult(!_outcomes.TryDequeue(out var outcome) || outcome());
            },
            policy,
            clock: _clock,
            backoffClock: _backoffClock,
            NullLogger.Instance,
            AbortToken
        );
    }

    /// <summary>Advances one interval per renewal, each time the loop has armed its next wait, and checks each ran.</summary>
    private async Task _TickAsync(int renewals, TimeSpan? step = null)
    {
        var target = Volatile.Read(ref _calls) + renewals;
        for (var tick = 0; tick < renewals; tick++)
        {
            await _clock.WaitForArmedTimerAsync();
            _clock.Advance(step ?? _Interval);
        }

        Volatile.Read(ref _calls).Should().BeGreaterThanOrEqualTo(target);
    }
}
