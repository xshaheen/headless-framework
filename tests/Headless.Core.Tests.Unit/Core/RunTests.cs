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
    public async Task should_execute_async_callback_with_retries_when_with_retries_async_is_called()
    {
        // given
        var attempts = 0;

        var callback = async () =>
        {
            attempts++;

            if (attempts < 3)
            {
                throw new InvalidOperationException("Retry");
            }

            await Task.CompletedTask;
        };

        // when
        var action = async () => await Run.WithRetriesAsync(callback, maxAttempts: 5);

        // then
        await action.Should().NotThrowAsync();
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task should_return_result_with_async_retries_when_with_retries_async_is_called()
    {
        // given
        var attempts = 0;

        async Task<int> callback()
        {
            attempts++;

            if (attempts < 3)
            {
                throw new InvalidOperationException("Retry");
            }

            await Task.CompletedTask;

            return 42;
        }

        // when
        var result = await Run.WithRetriesAsync(callback, maxAttempts: 5, cancellationToken: AbortToken);

        // then
        result.Should().Be(42);
        attempts.Should().Be(3);
    }

    #region DelayedAsync

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

    #endregion

    #region WithRetriesAsync

    [Fact]
    public async Task should_throw_after_max_attempts_exceeded()
    {
        // given
        var attempts = 0;
        const int maxAttempts = 3;

        async Task<int> callback(CancellationToken _)
        {
            attempts++;

            throw new InvalidOperationException("Always fails");
        }

        // when
        var action = async () =>
            await Run.WithRetriesAsync(callback, maxAttempts: maxAttempts, cancellationToken: AbortToken);

        // then
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("Always fails");
        attempts.Should().Be(maxAttempts);
    }

    [Fact]
    public async Task should_use_custom_retry_interval()
    {
        // given
        var fakeTime = new FakeTimeProvider();
        var attempts = 0;
        var retryInterval = TimeSpan.FromSeconds(5);

        async Task<int> callback(CancellationToken _)
        {
            attempts++;

            if (attempts < 3)
            {
                throw new InvalidOperationException("Retry");
            }

            return 42;
        }

        // when
        var task = Run.WithRetriesAsync(
            callback,
            maxAttempts: 5,
            retryInterval: retryInterval,
            timeProvider: fakeTime,
            cancellationToken: AbortToken
        );

        // first attempt fails immediately
        await Task.Delay(10, AbortToken);
        attempts.Should().Be(1);

        // advance time for first retry
        fakeTime.Advance(retryInterval);
        await Task.Delay(10, AbortToken);
        attempts.Should().Be(2);

        // advance time for second retry
        fakeTime.Advance(retryInterval);
        await Task.Delay(10, AbortToken);

        // then
        var result = await task;
        result.Should().Be(42);
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task should_respect_cancellation_during_retry()
    {
        // given
        using var cts = new CancellationTokenSource();
        var attempts = 0;

        async Task<int> callback(CancellationToken ct)
        {
            attempts++;

            if (attempts == 2)
            {
                await cts.CancelAsync();
            }

            throw new InvalidOperationException("Retry");
        }

        // when
        var action = async () => await Run.WithRetriesAsync(callback, maxAttempts: 10, cancellationToken: cts.Token);

        // then
        await action.Should().ThrowAsync<TaskCanceledException>();
    }

    [Fact]
    public async Task should_pass_state_to_action()
    {
        // given
        var state = new { Value = 42 };
        var receivedState = 0;

        async Task<int> callback(dynamic s, CancellationToken _)
        {
            receivedState = s.Value;

            await Task.CompletedTask;

            return s.Value * 2;
        }

        // when
        var result = await Run.WithRetriesAsync(state, callback, maxAttempts: 3, cancellationToken: AbortToken);

        // then
        receivedState.Should().Be(42);
        result.Should().Be(84);
    }

    [Fact]
    public async Task should_complete_void_action_without_return_value()
    {
        // given
        var executedCount = 0;

        async Task callback(CancellationToken _)
        {
            executedCount++;

            if (executedCount < 2)
            {
                throw new InvalidOperationException("Retry");
            }

            await Task.CompletedTask;
        }

        // when
        await Run.WithRetriesAsync(callback, maxAttempts: 5, cancellationToken: AbortToken);

        // then
        executedCount.Should().Be(2);
    }

    #endregion
}
