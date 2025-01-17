using System.Diagnostics;
using Framework.Core;

namespace Tests.Core;

public sealed class RunTests
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
        await Run.DelayedAsync(delay, action);
        var elapsed = Stopwatch.GetElapsedTime(timestamp);

        // then
        actionExecuted.Should().BeTrue();
        elapsed.Should().BeGreaterOrEqualTo(delay);
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
        var result = await Run.WithRetriesAsync(callback, maxAttempts: 5);

        // then
        result.Should().Be(42);
        attempts.Should().Be(3);
    }
}
