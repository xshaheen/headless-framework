// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.RequestReply;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.RequestReply;

/// <summary>
/// The requester's shutdown can run on another thread while a call registers. Whichever wins, a call that registered is
/// ended by the shutdown, and nothing it armed outlives it. The pending count, which the limit is checked against, frees
/// a call's slot exactly once however the call ends.
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
        registered.Should().Be(PendingRegistration.Registered);
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
        registered.Should().Be(PendingRegistration.Closed);
        pending.TrackedCount.Should().Be(0);
    }

    [Fact]
    public void should_refuse_a_call_over_the_limit_without_tracking_it()
    {
        // given
        var pending = new PendingRequests(maxPending: 2);
        var time = new FakeTimeProvider();
        pending.TryRegister(_Call("request-1", time), _Timeout, CancellationToken.None);
        pending.TryRegister(_Call("request-2", time), _Timeout, CancellationToken.None);

        // when
        var registered = pending.TryRegister(_Call("request-3", time), _Timeout, CancellationToken.None);

        // then
        registered.Should().Be(PendingRegistration.Full);
        pending.PendingCount.Should().Be(2);
        pending.TrackedCount.Should().Be(2);
        pending.TryGet("request-3", out _).Should().BeFalse();
    }

    [Fact]
    public void should_not_limit_the_calls_when_no_limit_is_set()
    {
        // given
        var pending = new PendingRequests();
        var time = new FakeTimeProvider();

        // when
        var registrations = Enumerable
            .Range(0, 1_000)
            .Select(i => pending.TryRegister(_Call($"request-{i}", time), _Timeout, CancellationToken.None))
            .ToList();

        // then
        registrations.Should().AllSatisfy(x => x.Should().Be(PendingRegistration.Registered));
        pending.PendingCount.Should().Be(1_000);
    }

    [Theory]
    [InlineData(CompletionPath.Reply)]
    [InlineData(CompletionPath.ReplyFailure)]
    [InlineData(CompletionPath.Timeout)]
    [InlineData(CompletionPath.Cancellation)]
    [InlineData(CompletionPath.Discard)]
    public void should_free_the_slot_when_a_call_ends(CompletionPath path)
    {
        // given — the only slot is taken
        var pending = new PendingRequests(maxPending: 1);
        var time = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var call = _Call("request-1", time);
        pending.TryRegister(call, _Timeout, cancellation.Token).Should().Be(PendingRegistration.Registered);
        pending
            .TryRegister(_Call("request-2", time), _Timeout, CancellationToken.None)
            .Should()
            .Be(PendingRegistration.Full);

        // when
        _End(path, pending, call, time, cancellation);

        // then
        pending.PendingCount.Should().Be(0);
        pending
            .TryRegister(_Call("request-3", time), _Timeout, CancellationToken.None)
            .Should()
            .Be(PendingRegistration.Registered);
    }

    [Fact]
    public async Task should_free_every_slot_when_the_requester_stops()
    {
        // given
        var pending = new PendingRequests(maxPending: 3);
        var time = new FakeTimeProvider();
        var calls = Enumerable.Range(0, 3).Select(i => _Call($"request-{i}", time)).ToList();
        foreach (var call in calls)
        {
            pending.TryRegister(call, _Timeout, CancellationToken.None);
        }

        // when
        pending.Close();

        // then
        pending.PendingCount.Should().Be(0);
        foreach (var call in calls)
        {
            await call.Awaiting(x => x.Outcome).Should().ThrowExactlyAsync<RequestAbortedException>();
        }
    }

    [Fact]
    public void should_free_the_slot_once_when_a_call_ends_again()
    {
        // given
        var pending = new PendingRequests(maxPending: 2);
        var time = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var other = _Call("request-other", time);
        var call = _Call("request-1", time);
        pending.TryRegister(other, TimeSpan.FromHours(1), CancellationToken.None);
        pending.TryRegister(call, _Timeout, cancellation.Token);

        // when — every path tries to end the same call after a reply already did
        call.TryClaimForReply().Should().BeTrue();
        call.CompleteClaimed(new PriceQuote(1m));
        call.TryEnd(new InvalidOperationException("late")).Should().BeFalse();
        cancellation.Cancel();
        time.Advance(_Timeout);
        pending.Discard(call);

        // then — only the call that ended gave its slot back; the other call still waits
        pending.PendingCount.Should().Be(1);
    }

    [Fact]
    public void should_admit_exactly_the_limit_when_calls_register_concurrently()
    {
        // given
        const int limit = 50;
        var pending = new PendingRequests(limit);
        var time = new FakeTimeProvider();
        var calls = Enumerable.Range(0, 1_000).Select(i => _Call($"request-{i}", time)).ToList();

        // when
        var registrations = new PendingRegistration[calls.Count];
        Parallel.For(
            0,
            calls.Count,
            new ParallelOptions { CancellationToken = AbortToken },
            i => registrations[i] = pending.TryRegister(calls[i], _Timeout, CancellationToken.None)
        );

        // then
        registrations.Count(x => x is PendingRegistration.Registered).Should().Be(limit);
        registrations.Count(x => x is PendingRegistration.Full).Should().Be(calls.Count - limit);
        pending.PendingCount.Should().Be(limit);
        pending.TrackedCount.Should().Be(limit);
    }

    [Fact]
    public void should_not_drift_when_every_completion_path_races_for_the_same_calls()
    {
        // given — calls that a reply, a failure, a cancellation, a timeout, a discard, and the shutdown all try to end
        var pending = new PendingRequests(maxPending: 500);
        var time = new FakeTimeProvider();
        var calls = Enumerable
            .Range(0, 500)
            .Select(i => (Call: _Call($"request-{i}", time), Cancellation: new CancellationTokenSource()))
            .ToList();
        foreach (var (call, cancellation) in calls)
        {
            pending.TryRegister(call, _Timeout, cancellation.Token).Should().Be(PendingRegistration.Registered);
        }

        var ends = calls
            .SelectMany(x =>
                new Action[]
                {
                    () => _End(CompletionPath.Reply, pending, x.Call, time, x.Cancellation),
                    () => _End(CompletionPath.ReplyFailure, pending, x.Call, time, x.Cancellation),
                    () => x.Call.TryEnd(new RequestTimeoutException(x.Call.RequestId, _Timeout)),
                    () => _End(CompletionPath.Cancellation, pending, x.Call, time, x.Cancellation),
                    () => _End(CompletionPath.Discard, pending, x.Call, time, x.Cancellation),
                }
            )
            .Append(() => time.Advance(_Timeout))
            .Append(pending.Close)
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        // when
        Parallel.ForEach(ends, new ParallelOptions { CancellationToken = AbortToken }, end => end());

        // then
        pending.PendingCount.Should().Be(0);
        foreach (var (call, cancellation) in calls)
        {
            call.State.Should().NotBe(PendingRequestState.Pending);
            cancellation.Dispose();
        }
    }

    public enum CompletionPath
    {
        Reply = 0,
        ReplyFailure = 1,
        Timeout = 2,
        Cancellation = 3,
        Discard = 4,
    }

    private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(30);

    private static PendingRequest _Call(string requestId, TimeProvider time)
    {
        return new PendingRequest(requestId, typeof(PriceQuote), "pricing.quote", "1", _Timeout, time);
    }

    private static void _End(
        CompletionPath path,
        PendingRequests pending,
        PendingRequest call,
        FakeTimeProvider time,
        CancellationTokenSource cancellation
    )
    {
        switch (path)
        {
            case CompletionPath.Reply when call.TryClaimForReply():
                call.CompleteClaimed(new PriceQuote(1m));
                break;
            case CompletionPath.ReplyFailure when call.TryClaimForReply():
                call.FailClaimed(new InvalidOperationException("contract mismatch"));
                break;
            case CompletionPath.Timeout:
                time.Advance(_Timeout);
                break;
            case CompletionPath.Cancellation:
                cancellation.Cancel();
                break;
            case CompletionPath.Discard:
                pending.Discard(call);
                break;
        }
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
