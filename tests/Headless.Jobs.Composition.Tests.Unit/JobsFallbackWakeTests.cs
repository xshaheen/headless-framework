// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.BackgroundServices;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// The fallback loop's idle wait ends at its period or when the scheduler requests a pass, and a request is never lost:
/// one made while no pass waits starts the next wait at once.
/// </summary>
public sealed class JobsFallbackWakeTests : TestBase
{
    private static readonly TimeSpan _Period = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan _Bound = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task should_end_the_wait_when_a_pass_is_requested_before_the_period()
    {
        // given: a clock that never advances, so only the request can end the wait
        var wake = new JobsFallbackWake();
        var waiting = wake.WaitAsync(_Period, new FakeTimeProvider(), AbortToken);
        waiting.IsCompleted.Should().BeFalse();

        // when
        wake.Request();

        // then
        await waiting.WaitAsync(_Bound, AbortToken);
    }

    [Fact]
    public async Task should_keep_a_request_made_while_no_pass_waits_for_the_next_wait()
    {
        // given
        var wake = new JobsFallbackWake();
        wake.Request();

        // when / then
        await wake.WaitAsync(_Period, new FakeTimeProvider(), AbortToken).WaitAsync(_Bound, AbortToken);
    }

    [Fact]
    public async Task should_end_the_wait_at_the_period_and_leave_no_request_behind()
    {
        // given
        var wake = new JobsFallbackWake();
        var clock = new FakeTimeProvider();
        var first = wake.WaitAsync(_Period, clock, AbortToken);

        // when
        clock.Advance(_Period);
        await first.WaitAsync(_Bound, AbortToken);

        // then: the period ended the wait, so the next one waits for its own period
        var second = wake.WaitAsync(_Period, clock, AbortToken);
        second.IsCompleted.Should().BeFalse();
        clock.Advance(_Period);
        await second.WaitAsync(_Bound, AbortToken);
    }

    [Fact]
    public async Task should_end_the_wait_with_cancellation()
    {
        // given
        var wake = new JobsFallbackWake();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
#pragma warning disable AsyncFixer04 // False positive: the wait is awaited below, before the token source is disposed.
        var waiting = wake.WaitAsync(_Period, new FakeTimeProvider(), stop.Token);
#pragma warning restore AsyncFixer04

        // when
        await stop.CancelAsync();

        // then
        var act = () => waiting.WaitAsync(_Bound, AbortToken);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
