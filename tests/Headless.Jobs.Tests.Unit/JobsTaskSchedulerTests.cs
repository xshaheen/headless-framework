// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;
using Headless.Jobs.JobsThreadPool;

namespace Tests;

public sealed class JobsTaskSchedulerTests
{
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
            TestContext.Current.CancellationToken
        );

        await executed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // Give the now-idle worker time to enter the backoff delay await.
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var dispose = scheduler.DisposeAsync().AsTask();
        var finished = await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(10)));

        finished.Should().Be(dispose);
        await dispose;
        scheduler.ActiveWorkers.Should().Be(0);
    }
}
