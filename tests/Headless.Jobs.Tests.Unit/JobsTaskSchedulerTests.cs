// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;

namespace Tests;

public sealed class JobsTaskSchedulerTests : TestBase
{
    [Fact]
    public async Task WaitForRunningTasksAsync_waits_for_active_async_work()
    {
        await using var scheduler = new JobsTaskScheduler(
            maxConcurrency: 1,
            idleWorkerTimeout: TimeSpan.FromMilliseconds(25)
        );
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.QueueAsync(
            async _ =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);
            },
            JobPriority.Normal,
            AbortToken
        );

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromMilliseconds(50))).Should().BeFalse();

        release.SetResult();

        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        scheduler.ActiveTasks.Should().Be(0);
    }

    [Fact]
    public async Task QueueAsync_counts_active_async_work_against_max_concurrency()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var twoStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedCount = 0;
        var activeCount = 0;
        var maxObservedActive = 0;

        for (var i = 0; i < 5; i++)
        {
            await scheduler.QueueAsync(
                async _ =>
                {
                    var active = Interlocked.Increment(ref activeCount);
                    _SetMaxObserved(ref maxObservedActive, active);

                    if (Interlocked.Increment(ref startedCount) == 2)
                    {
                        twoStarted.TrySetResult();
                    }

                    await release.Task.ConfigureAwait(false);
                    Interlocked.Decrement(ref activeCount);
                },
                JobPriority.Normal,
                AbortToken
            );
        }

        await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        await Task.Delay(100, AbortToken);

        startedCount.Should().Be(2);
        maxObservedActive.Should().BeLessThanOrEqualTo(2);

        release.SetResult();

        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        startedCount.Should().Be(5);
        maxObservedActive.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task Dispose_while_worker_idles_in_backoff_completes_cleanly()
    {
        // A worker that finished its work parks in the idle backoff delay awaiting the shutdown
        // token. DisposeAsync cancels that token; the resulting OperationCanceledException must
        // not escape the worker's thread-start delegate — an unhandled exception on a manually
        // created thread terminates the whole process.
        var scheduler = new JobsTaskScheduler(maxConcurrency: 2);
        var executed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.QueueAsync(
            _ =>
            {
                executed.TrySetResult();

                return Task.CompletedTask;
            },
            JobPriority.Normal,
            AbortToken
        );

        await executed.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // Give the now-idle worker time to enter the backoff delay await.
        await Task.Delay(100, AbortToken);

        var dispose = scheduler.DisposeAsync().AsTask();
        var finished = await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(10), AbortToken));

        finished.Should().Be(dispose);
        await dispose;
        scheduler.ActiveWorkers.Should().Be(0);
    }

    private static void _SetMaxObserved(ref int target, int value)
    {
        int current;

        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }
}
