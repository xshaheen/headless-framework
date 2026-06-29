using System.Diagnostics;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Threading;

public sealed class HeadlessTaskExtensionsTests : TestBase
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
        await Task.DelayedAsync(delay, action, cancellationToken: AbortToken);
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
        var action = async () => await Task.DelayedAsync(delay, _ => Task.CompletedTask, cancellationToken: AbortToken);

        // then
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_when_action_is_null()
    {
        // given
        var delay = TimeSpan.FromMilliseconds(100);

        // when
        var action = async () => await Task.DelayedAsync(delay, null!, cancellationToken: AbortToken);

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
        var action = async () => await Task.DelayedAsync(delay, _ => Task.CompletedTask, cancellationToken: cts.Token);

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
        var task = Task.DelayedAsync(delay, action, timeProvider: fakeTime, cancellationToken: AbortToken);

        // then - action should not execute before advancing time
        await Task.Delay(50, AbortToken);
        actionExecuted.Should().BeFalse();

        // advance time past the delay
        fakeTime.Advance(delay + TimeSpan.FromMilliseconds(100));
        await task;

        actionExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task with_cancellation_returns_result_when_task_completes_before_cancellation()
    {
        // given
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var wrapped = tcs.Task.WithCancellation(cts.Token);

        // when
        tcs.SetResult(42);

        // then
        (await wrapped)
            .Should()
            .Be(42);
    }

    [Fact]
    public async Task with_cancellation_throws_when_token_cancels_before_task_completes()
    {
        // given
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var wrapped = tcs.Task.WithCancellation(cts.Token);

        // when
        await cts.CancelAsync();

        // then
        var act = async () => await wrapped;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task with_cancellation_non_generic_throws_when_token_cancels_before_task_completes()
    {
        // given
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var wrapped = tcs.Task.WithCancellation(cts.Token);

        // when
        await cts.CancelAsync();

        // then
        var act = async () => await wrapped;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task with_cancellation_returns_same_task_when_token_cannot_be_cancelled()
    {
        // given
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = tcs.Task;

        // when — a non-cancellable token short-circuits to the same task instance (no wrapper allocation)
        var wrapped = task.WithCancellation(CancellationToken.None);

        // then
        wrapped.Should().BeSameAs(task);
        tcs.SetResult(7);
        (await wrapped).Should().Be(7);
    }

    [Fact]
    public async Task with_cancellation_observes_abandoned_task_fault_after_cancellation_wins()
    {
        // given
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var wrapped = tcs.Task.WithCancellation(cts.Token);

        // when — cancellation wins the race, abandoning the wrapped task
        await cts.CancelAsync();
        var act = async () => await wrapped;
        await act.Should().ThrowAsync<OperationCanceledException>();

        // then — faulting the abandoned task afterwards is observed (via Forget), not left to escalate
        tcs.SetException(new InvalidOperationException("late fault"));
        await Task.Yield();
        tcs.Task.IsFaulted.Should().BeTrue();
    }
}
