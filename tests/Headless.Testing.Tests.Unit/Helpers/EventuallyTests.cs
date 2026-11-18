// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Helpers;

public sealed class EventuallyTests : TestBase
{
    private static readonly TimeSpan _Limit = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task should_return_without_waiting_when_the_condition_already_holds()
    {
        var clock = new FakeTimeProvider();
        var probes = 0;

        await Eventually.UntilAsync(() => ++probes == 1, timeProvider: clock, cancellationToken: AbortToken);

        probes.Should().Be(1);
    }

    [Fact]
    public async Task should_probe_again_until_the_condition_holds()
    {
        var probes = 0;

        await Eventually.UntilAsync(
            () => ++probes == 3,
            interval: TimeSpan.FromMilliseconds(1),
            cancellationToken: AbortToken
        );

        probes.Should().Be(3);
    }

    [Fact]
    public async Task should_return_the_first_value_that_satisfies_the_condition()
    {
        var reads = 0;

        var value = await Eventually.UntilAsync(
            () => ++reads,
            n => n >= 3,
            interval: TimeSpan.FromMilliseconds(1),
            cancellationToken: AbortToken
        );

        value.Should().Be(3);
    }

    [Fact]
    public async Task should_return_the_first_value_an_async_probe_reads_that_satisfies_the_condition()
    {
        var reads = 0;

        var value = await Eventually.UntilAsync(
            _ => Task.FromResult(++reads),
            n => n >= 2,
            interval: TimeSpan.FromMilliseconds(1),
            cancellationToken: AbortToken
        );

        value.Should().Be(2);
    }

    [Fact]
    public async Task should_complete_when_an_async_condition_holds()
    {
        var probes = 0;

        await Eventually.UntilAsync(
            _ => Task.FromResult(++probes == 2),
            interval: TimeSpan.FromMilliseconds(1),
            cancellationToken: AbortToken
        );

        probes.Should().Be(2);
    }

    [Fact]
    public async Task should_throw_a_timeout_naming_the_condition_when_it_never_holds()
    {
        var clock = new FakeTimeProvider();
        const bool ready = false;

        var wait = Eventually.UntilAsync(() => ready, _Limit, timeProvider: clock, cancellationToken: AbortToken);
        clock.Advance(_Limit);

        var act = () => wait;
        (await act.Should().ThrowAsync<TimeoutException>()).WithMessage(
            "Waited 1 s for '() => ready' to hold; the last of 2 probe(s) returned False."
        );
    }

    [Fact]
    public async Task should_throw_a_timeout_holding_the_last_value_read()
    {
        var clock = new FakeTimeProvider();
        const string status = "pending";

        var wait = Eventually.UntilAsync(
            () => status,
            s => string.Equals(s, "done", StringComparison.Ordinal),
            _Limit,
            timeProvider: clock,
            cancellationToken: AbortToken
        );
        clock.Advance(_Limit);

        var act = () => wait;
        (await act.Should().ThrowAsync<TimeoutException>()).WithMessage(
            "Waited 1 s for '() => status' to satisfy 's => string.Equals(s, \"done\", StringComparison.Ordinal)'; the last of 2 probe(s) returned \"pending\"."
        );
    }

    [Fact]
    public async Task should_throw_a_timeout_when_an_async_probe_hangs_past_the_limit()
    {
        var clock = new FakeTimeProvider();
        var never = new TaskCompletionSource<int>();

        var wait = Eventually.UntilAsync(
            ct => never.Task.WaitAsync(ct),
            n => n > 0,
            _Limit,
            timeProvider: clock,
            cancellationToken: AbortToken
        );
        clock.Advance(_Limit);

        var act = () => wait;
        (await act.Should().ThrowAsync<TimeoutException>())
            .WithMessage(
                "Waited 1 s for 'ct => never.Task.WaitAsync(ct)' to satisfy 'n => n > 0'; the probe did not return."
            )
            .WithInnerException<OperationCanceledException>();
    }

    [Fact]
    public async Task should_throw_a_timeout_when_an_async_condition_hangs_past_the_limit()
    {
        var clock = new FakeTimeProvider();
        var never = new TaskCompletionSource<bool>();

        var wait = Eventually.UntilAsync(
            ct => never.Task.WaitAsync(ct),
            _Limit,
            timeProvider: clock,
            cancellationToken: AbortToken
        );
        clock.Advance(_Limit);

        var act = () => wait;
        await act.Should().ThrowAsync<TimeoutException>().WithMessage("*'ct => never.Task.WaitAsync(ct)' to hold*");
    }

    [Fact]
    public async Task should_stop_when_the_wait_is_cancelled()
    {
        var clock = new FakeTimeProvider();
        var cancelled = new CancellationToken(canceled: true);

        var act = () => Eventually.UntilAsync(() => false, timeProvider: clock, cancellationToken: cancelled);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_not_report_a_timeout_when_the_wait_is_cancelled_while_an_async_probe_runs()
    {
        var clock = new FakeTimeProvider();
        var cancelled = new CancellationToken(canceled: true);
        var never = new TaskCompletionSource<bool>();

        var act = () =>
            Eventually.UntilAsync(ct => never.Task.WaitAsync(ct), timeProvider: clock, cancellationToken: cancelled);

        await act.Should().ThrowExactlyAsync<TaskCanceledException>();
    }

    [Fact]
    public async Task should_propagate_a_probe_failure()
    {
        var act = () =>
            Eventually.UntilAsync<int>(
                () => throw new InvalidOperationException("boom"),
                n => n > 0,
                cancellationToken: AbortToken
            );

        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task should_reject_a_non_positive_interval()
    {
        var act = () => Eventually.UntilAsync(() => true, interval: TimeSpan.Zero, cancellationToken: AbortToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void should_default_to_a_ten_millisecond_interval()
    {
        Eventually.DefaultInterval.Should().Be(TimeSpan.FromMilliseconds(10));
    }
}
