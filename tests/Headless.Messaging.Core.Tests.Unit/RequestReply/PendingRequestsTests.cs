// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.RequestReply;
using Headless.Testing.Tests;

namespace Tests.RequestReply;

/// <summary>
/// The requester's shutdown can run on another thread while a call registers. Whichever wins, a call that registered is
/// ended by the shutdown, and nothing it armed outlives it.
/// </summary>
public sealed class PendingRequestsTests : TestBase
{
    [Fact]
    public async Task should_release_the_timeout_timer_when_the_requester_stops_while_a_call_registers()
    {
        // given — a clock that starts the shutdown on another thread while the call's timeout timer is being created
        var pending = new PendingRequests();
        var clock = new ClosingWhileArmingTimeProvider(pending);
        var call = new PendingRequest(
            "request-1",
            typeof(PriceQuote),
            "pricing.quote",
            "1",
            TimeSpan.FromSeconds(30),
            clock
        );

        // when
        var registered = pending.TryRegister(call, TimeSpan.FromSeconds(30), CancellationToken.None);
        await clock.Closed.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then — the shutdown ended the call and released the timer the call armed
        registered.Should().BeTrue();
        await call.Awaiting(x => x.Outcome).Should().ThrowExactlyAsync<RequestAbortedException>();
        clock.LiveTimeoutTimers.Should().Be(0);
    }

    [Fact]
    public void should_refuse_a_call_once_the_requester_stopped()
    {
        // given
        var pending = new PendingRequests();
        pending.Close();
        var call = new PendingRequest(
            "request-1",
            typeof(PriceQuote),
            "pricing.quote",
            "1",
            TimeSpan.FromSeconds(30),
            TimeProvider.System
        );

        // when
        var registered = pending.TryRegister(call, TimeSpan.FromSeconds(30), CancellationToken.None);

        // then
        registered.Should().BeFalse();
        pending.TrackedCount.Should().Be(0);
    }

    /// <summary>
    /// Starts <see cref="PendingRequests.Close"/> on another thread from inside the first call timer it creates, and
    /// gives that close a moment to finish before the timer is handed back, so a close that is not held off by the
    /// registration runs in the middle of it. It counts the call timers still live.
    /// </summary>
    private sealed class ClosingWhileArmingTimeProvider(PendingRequests pending) : TimeProvider
    {
        private int _liveTimeoutTimers;
        private Task? _close;

        public Task Closed => _close ?? Task.CompletedTask;

        public int LiveTimeoutTimers => Volatile.Read(ref _liveTimeoutTimers);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            if (state is not PendingRequest)
            {
                return timer;
            }

            Interlocked.Increment(ref _liveTimeoutTimers);
            if (_close is null)
            {
                _close = Task.Run(pending.Close);
                SpinWait.SpinUntil(() => _close.IsCompleted, TimeSpan.FromMilliseconds(200));
            }

            return new CountedTimer(timer, () => Interlocked.Decrement(ref _liveTimeoutTimers));
        }
    }

    private sealed class CountedTimer(ITimer inner, Action onDisposed) : ITimer
    {
        private int _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            return inner.Change(dueTime, period);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                onDisposed();
            }

            inner.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
