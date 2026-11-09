// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Threading.Channels;
using Headless.Fencing;
using Headless.Messaging;
using Headless.Messaging.Processor;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Helpers;

namespace Tests.Processor;

public sealed class ClaimedBatchLeaseRenewalTests : TestBase
{
    private static readonly TimeSpan _Interval = TimeSpan.FromSeconds(10);
    private readonly TimerArmedSignalClock _clock = new(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly ConcurrentQueue<Guid> _calls = new();
    private readonly Channel<bool> _called = Channel.CreateUnbounded<bool>();

    [Fact]
    public async Task should_stop_renewing_a_row_once_its_lease_is_lost()
    {
        var lost = _CreateMessage();
        var kept = _CreateMessage();
        await using var renewal = _Start(
            [lost, kept],
            message => ValueTask.FromResult(!ReferenceEquals(message, lost))
        );

        await _TickAsync(expectedCalls: 2);
        await _TickAsync(expectedCalls: 1);
        await _TickAsync(expectedCalls: 1);

        _calls.Count(id => id == lost.StorageId).Should().Be(1);
        _calls.Count(id => id == kept.StorageId).Should().Be(3);
    }

    [Fact]
    public async Task should_stop_the_loop_once_every_waiting_row_is_lost()
    {
        var message = _CreateMessage();
        await using var renewal = _Start([message], _ => ValueTask.FromResult(false));

        await _TickAsync(expectedCalls: 1);

        // The loop ended on its own, so stopping it waits for nothing and arms no further renewal.
        await renewal.StopAsync().Bounded();
        _calls.Should().ContainSingle();
    }

    [Fact]
    public async Task should_retry_a_row_on_the_next_tick_when_its_renewal_throws()
    {
        var message = _CreateMessage();
        var attempts = 0;
        await using var renewal = _Start(
            [message],
            _ =>
                Interlocked.Increment(ref attempts) == 1
                    ? ValueTask.FromException<bool>(new InvalidOperationException("store blip"))
                    : ValueTask.FromResult(true)
        );

        await _TickAsync(expectedCalls: 1);
        await _TickAsync(expectedCalls: 1);

        _calls.Should().HaveCount(2).And.AllBeEquivalentTo(message.StorageId);
    }

    [Fact]
    public async Task should_stop_renewing_when_the_store_cannot_renew()
    {
        var first = _CreateMessage();
        var second = _CreateMessage();
        await using var renewal = _Start(
            [first, second],
            _ => ValueTask.FromException<bool>(new NotSupportedException())
        );

        await _TickAsync(expectedCalls: 1);
        await renewal.StopAsync();

        _calls.Should().ContainSingle();
    }

    [Fact]
    public async Task should_not_renew_a_row_taken_for_dispatch()
    {
        var taken = _CreateMessage();
        var waiting = _CreateMessage();
        await using var renewal = _Start([taken, waiting], _ => ValueTask.FromResult(true));

        await renewal.TakeAsync(taken);
        await _TickAsync(expectedCalls: 1);
        await renewal.StopAsync();

        _calls.Should().Equal(waiting.StorageId);
    }

    private ClaimedBatchLeaseRenewal _Start(
        IEnumerable<MediumMessage> messages,
        Func<MediumMessage, ValueTask<bool>> renew
    )
    {
        return ClaimedBatchLeaseRenewal.Start(
            messages,
            (message, _, _) =>
            {
                _calls.Enqueue(message.StorageId);
                _called.Writer.TryWrite(true);
                return renew(message);
            },
            new RetryPolicyOptions { DispatchTimeout = TimeSpan.FromSeconds(30), LeaseRenewalInterval = _Interval },
            _clock,
            TimeProvider.System,
            NullLogger.Instance
        );
    }

    // One renewal pass: advance once the loop has armed its delay, then wait for the pass's renewal calls.
    private async Task _TickAsync(int expectedCalls)
    {
        await _clock.AdvanceWhenArmedAsync(_Interval);
        for (var call = 0; call < expectedCalls; call++)
        {
            await _called.Reader.ReadAsync(AbortToken).Bounded();
        }
    }

    private static MediumMessage _CreateMessage()
    {
        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(new Dictionary<string, string?>(StringComparer.Ordinal), null),
            Content = "{}",
            Lane = MessageLane.Bus,
            Lease = new RowLeaseSnapshot(1, RowLeaseState.Active, DateTimeOffset.UtcNow.AddSeconds(30), "node-a", 0),
        };
    }
}
