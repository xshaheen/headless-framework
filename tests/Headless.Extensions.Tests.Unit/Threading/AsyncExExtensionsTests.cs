// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using Nito.AsyncEx;

namespace Tests.Threading;

// ReSharper disable AccessToDisposedClosure
public sealed class AsyncExExtensionsTests : TestBase
{
    #region SafeWaitAsync(CancellationToken) — swallows the wait-token cancellation

    // NOTE: the CT overloads receive a single opaque token and cannot tell a caller token from a
    // timeout/linked token. In-repo callers (DistributedLocks LeaseMonitor/DistributedLock/Semaphore)
    // pass timeout/linked tokens and rely on this swallow as the normal loop tick, so the overloads
    // intentionally swallow cancellation here.

    [Fact]
    public async Task safe_wait_async_swallows_cancellation_for_auto_reset_event()
    {
        // given — an event that is never set and an already-cancelled wait token
        var resetEvent = new AsyncAutoResetEvent();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await resetEvent.SafeWaitAsync(cts.Token);

        // then — the CT overload returns silently when its wait token is cancelled
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task safe_wait_async_swallows_cancellation_for_manual_reset_event()
    {
        // given
        var resetEvent = new AsyncManualResetEvent();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await resetEvent.SafeWaitAsync(cts.Token);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task safe_wait_async_swallows_cancellation_for_countdown_event()
    {
        // given
        var countdownEvent = new AsyncCountdownEvent(1);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await countdownEvent.SafeWaitAsync(cts.Token);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task safe_wait_async_completes_without_throwing_when_event_is_already_set()
    {
        // given — event already set (ctor arg), token never cancelled
        var resetEvent = new AsyncManualResetEvent(true);

        // when
        var act = async () => await resetEvent.SafeWaitAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region SafeWaitAsync(timeout) — still swallows the internal timeout

    [Fact]
    public async Task safe_wait_async_swallows_timeout_for_auto_reset_event()
    {
        // given — an event that is never set, so the internal timeout token fires
        var resetEvent = new AsyncAutoResetEvent();

        // when
        var act = async () => await resetEvent.SafeWaitAsync(TimeSpan.FromMilliseconds(10));

        // then — the timeout overload returns silently (the OCE is from the internal token, not a caller)
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region WaitAsync(AsyncCountdownEvent, timeout) — cancels the loser

    [Fact]
    public async Task countdown_wait_async_returns_when_event_signals_before_timeout()
    {
        // given — a long timeout driven by a fake clock that we never advance
        var clock = new FakeTimeProvider();
        var countdownEvent = new AsyncCountdownEvent(1);
        var waiter = countdownEvent.WaitAsync(TimeSpan.FromSeconds(30), clock);

        // when — signalling completes the wait; the delay timer must be cancelled so this returns promptly
        countdownEvent.Signal();

        // then — completes without advancing the clock
        var completed = await Task.WhenAny(waiter, Task.Delay(TimeSpan.FromSeconds(5), AbortToken));
        completed.Should().BeSameAs(waiter);
        await waiter;
    }

    [Fact]
    public async Task countdown_wait_async_returns_without_throwing_when_timeout_elapses()
    {
        // given — a countdown that never signals, so only the timeout can complete the wait
        var clock = new FakeTimeProvider();
        var countdownEvent = new AsyncCountdownEvent(1);
        var waiter = countdownEvent.WaitAsync(TimeSpan.FromSeconds(30), clock);

        // when — advancing the provided clock fires the timeout
        clock.Advance(TimeSpan.FromSeconds(30));

        // then — the timeout path returns without throwing
        var act = async () => await waiter;
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task countdown_wait_async_does_not_time_out_on_real_time_when_using_fake_clock()
    {
        // given — timeout is scheduled on the fake clock, so real time passing must not complete it
        var clock = new FakeTimeProvider();
        var countdownEvent = new AsyncCountdownEvent(1);

        // when
        var waiter = countdownEvent.WaitAsync(TimeSpan.FromSeconds(30), clock);
        await Task.Delay(TimeSpan.FromMilliseconds(50), AbortToken);

        // then — neither signalled nor advanced, so the wait is still pending
        waiter.IsCompleted.Should().BeFalse();

        // cleanup
        countdownEvent.Signal();
        await waiter;
    }

    #endregion
}
