using System.Diagnostics;
using Headless.Core;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Core;

public sealed class RunTests : TestBase
{
    [Fact]
    public async Task should_complete_task_after_delay_when_delayed_async_is_called()
    {
        // given
        var delay = TimeSpan.FromMilliseconds(200);
        var actionExecuted = false;

        async Task action(CancellationToken _)
        {
            actionExecuted = true;

            await Task.CompletedTask;
        }

        // when
        var timestamp = Stopwatch.GetTimestamp();
        await Run.DelayedAsync(delay, action, cancellationToken: AbortToken);
        var elapsed = Stopwatch.GetElapsedTime(timestamp);

        // then
        actionExecuted.Should().BeTrue();
        elapsed.Should().BeGreaterThanOrEqualTo(delay);
    }

    [Fact]
    public async Task should_throw_when_delay_is_negative()
    {
        // given
        var delay = TimeSpan.FromMilliseconds(-100);

        // when
        var action = async () => await Run.DelayedAsync(delay, _ => Task.CompletedTask, cancellationToken: AbortToken);

        // then
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_when_action_is_null()
    {
        // given
        var delay = TimeSpan.FromMilliseconds(100);

        // when
        var action = async () => await Run.DelayedAsync(delay, null!, cancellationToken: AbortToken);

        // then
        await action.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_cancel_when_token_cancelled_before_delay()
    {
        // given
        var delay = TimeSpan.FromMilliseconds(1000);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var action = async () => await Run.DelayedAsync(delay, _ => Task.CompletedTask, cancellationToken: cts.Token);

        // then
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_use_custom_time_provider()
    {
        // given
        var fakeTime = new FakeTimeProvider();
        var delay = TimeSpan.FromSeconds(10);
        var actionExecuted = false;

        async Task action(CancellationToken _)
        {
            actionExecuted = true;

            await Task.CompletedTask;
        }

        // when
        var task = Run.DelayedAsync(delay, action, timeProvider: fakeTime, cancellationToken: AbortToken);

        // then - action should not execute before advancing time
        await Task.Delay(50, AbortToken);
        actionExecuted.Should().BeFalse();

        // advance time past the delay
        fakeTime.Advance(delay + TimeSpan.FromMilliseconds(100));
        await task;

        actionExecuted.Should().BeTrue();
    }
}
