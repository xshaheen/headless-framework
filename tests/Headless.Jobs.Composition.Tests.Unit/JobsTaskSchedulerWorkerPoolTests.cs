// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Jobs;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;

namespace Tests;

public sealed class JobsTaskSchedulerWorkerPoolTests : TestBase
{
    private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task idle_scheduler_arms_no_timers()
    {
        var timeProvider = new CountingTimeProvider();
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 4, timeProvider);
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.QueueAsync(
            _ =>
            {
                ran.TrySetResult();
                return Task.CompletedTask;
            },
            JobPriority.Normal,
            AbortToken
        );
        await ran.Task.WaitAsync(_Timeout, AbortToken);

        // Let the worker leave its short spin phase and park.
        await Task.Delay(TimeSpan.FromMilliseconds(500), AbortToken);
        var before = timeProvider.TimerCount;

        await Task.Delay(TimeSpan.FromSeconds(1), AbortToken);

        // Polling armed a timer every few tens of milliseconds per worker.
        (timeProvider.TimerCount - before)
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task enqueue_wakes_a_parked_worker()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 2, TimeProvider.System);
        await _RunAsync(scheduler, null);
        await Task.Delay(TimeSpan.FromMilliseconds(500), AbortToken);

        for (var i = 0; i < 20; i++)
        {
            await _RunAsync(scheduler, null).WaitAsync(_Timeout, AbortToken);
        }
    }

    [Fact]
    public async Task idle_worker_still_retires_after_the_idle_timeout()
    {
        await using var scheduler = new JobsTaskScheduler(
            maxConcurrency: 3,
            TimeProvider.System,
            idleWorkerTimeout: TimeSpan.FromMilliseconds(50)
        );
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        for (var i = 0; i < 3; i++)
        {
            await scheduler.QueueAsync(
                async _ =>
                {
                    Interlocked.Increment(ref started);
                    await release.Task;
                },
                JobPriority.Normal,
                AbortToken
            );
        }

        await _WaitUntilAsync(() => Volatile.Read(ref started) == 3);
        release.SetResult();

        await _WaitUntilAsync(() => scheduler.ActiveWorkers == 1);
    }

    [Fact]
    public async Task capped_burst_does_not_block_other_functions()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 4, TimeProvider.System);
        using var gate = new SemaphoreSlim(1, 1);
        var releaseCapped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cappedStarted = 0;
        var uncappedDone = 0;

        for (var i = 0; i < 12; i++)
        {
            await scheduler.QueueAsync(
                async _ =>
                {
                    Interlocked.Increment(ref cappedStarted);
                    await releaseCapped.Task;
                },
                JobPriority.Normal,
                AbortToken,
                AbortToken,
                gate
            );
        }

        for (var i = 0; i < 12; i++)
        {
            await scheduler.QueueAsync(
                _ =>
                {
                    Interlocked.Increment(ref uncappedDone);
                    return Task.CompletedTask;
                },
                JobPriority.Normal,
                AbortToken
            );
        }

        // Every uncapped job finishes while the first capped job still holds the only permit. Before the gate moved
        // into the scheduler, the parked capped jobs held all four worker slots and nothing else ran.
        await _WaitUntilAsync(() => Volatile.Read(ref uncappedDone) == 12);
        Volatile.Read(ref cappedStarted).Should().Be(1);
        scheduler.ParkedTasks.Should().Be(11);

        releaseCapped.SetResult();
        (await scheduler.WaitForRunningTasksAsync(_Timeout)).Should().BeTrue();
        scheduler.ParkedTasks.Should().Be(0);
    }

    [Fact]
    public async Task parked_items_run_in_arrival_order()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 4, TimeProvider.System);
        using var gate = new SemaphoreSlim(1, 1);
        var order = new ConcurrentQueue<int>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        for (var i = 0; i < 8; i++)
        {
            var id = i;
            await scheduler.QueueAsync(
                async _ =>
                {
                    order.Enqueue(id);
                    await release.Task;
                },
                JobPriority.Normal,
                AbortToken,
                AbortToken,
                gate
            );

            // Wait for the item to run or park so arrival order at the gate equals enqueue order.
            await _WaitUntilAsync(() => order.Count + scheduler.ParkedTasks == id + 1);
        }

        release.SetResult();
        (await scheduler.WaitForRunningTasksAsync(_Timeout)).Should().BeTrue();
        order.Should().Equal(0, 1, 2, 3, 4, 5, 6, 7);
    }

    [Fact]
    public async Task parked_items_count_as_queued_work_for_drains()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 2, TimeProvider.System);
        using var gate = new SemaphoreSlim(1, 1);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        for (var i = 0; i < 4; i++)
        {
            await scheduler.QueueAsync(_ => release.Task, JobPriority.Normal, AbortToken, AbortToken, gate);
        }

        // The permit holder still counts as queued between taking the permit and starting, so the counts settle
        // only once it is active.
        await _WaitUntilAsync(() => scheduler.ParkedTasks == 3 && scheduler.ActiveTasks == 1);
        scheduler.TotalQueuedTasks.Should().Be(3);
        scheduler.GetDiagnostics().Should().NotContain("DISCREPANCY");
        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromMilliseconds(100))).Should().BeFalse();

        release.SetResult();
        (await scheduler.WaitForRunningTasksAsync(_Timeout)).Should().BeTrue();
    }

    [Fact]
    public async Task frozen_scheduler_still_runs_parked_items()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 2, TimeProvider.System);
        using var gate = new SemaphoreSlim(1, 1);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;

        for (var i = 0; i < 4; i++)
        {
            await scheduler.QueueAsync(
                async _ =>
                {
                    await release.Task;
                    Interlocked.Increment(ref completed);
                },
                JobPriority.Normal,
                AbortToken,
                AbortToken,
                gate
            );
        }

        await _WaitUntilAsync(() => scheduler.ParkedTasks == 3);

        // The shutdown drain freezes the pool and then waits for admitted work, parked items included.
        scheduler.Freeze();
        release.SetResult();

        (await scheduler.WaitForRunningTasksAsync(_Timeout)).Should().BeTrue();
        completed.Should().Be(4);
    }

    [Fact]
    public async Task dispose_with_parked_items_completes()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 2, TimeProvider.System);
        using var gate = new SemaphoreSlim(1, 1);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        for (var i = 0; i < 4; i++)
        {
            await scheduler.QueueAsync(
                async ct => await release.Task.WaitAsync(ct),
                JobPriority.Normal,
                AbortToken,
                AbortToken,
                gate
            );
        }

        await _WaitUntilAsync(() => scheduler.ParkedTasks == 3);

        var dispose = scheduler.DisposeAsync().AsTask();
        (await Task.WhenAny(dispose, Task.Delay(_Timeout, AbortToken))).Should().Be(dispose);
        await dispose;
    }

    [Fact]
    public async Task long_running_work_waits_for_its_function_gate()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 2, TimeProvider.System);
        using var gate = new SemaphoreSlim(1, 1);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        var maxRunning = 0;
        var completed = 0;

        for (var i = 0; i < 3; i++)
        {
            await scheduler.QueueAsync(
                async _ =>
                {
                    var now = Interlocked.Increment(ref running);
                    _SetMax(ref maxRunning, now);
                    await release.Task;
                    Interlocked.Decrement(ref running);
                    Interlocked.Increment(ref completed);
                },
                JobPriority.LongRunning,
                AbortToken,
                AbortToken,
                gate
            );
        }

        await _WaitUntilAsync(() => Volatile.Read(ref running) == 1);
        release.SetResult();
        await _WaitUntilAsync(() => Volatile.Read(ref completed) == 3);

        maxRunning.Should().Be(1);
    }

    private static void _SetMax(ref int target, int value)
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

    private async Task _RunAsync(JobsTaskScheduler scheduler, SemaphoreSlim? gate)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.QueueAsync(
            _ =>
            {
                done.TrySetResult();
                return Task.CompletedTask;
            },
            JobPriority.Normal,
            AbortToken,
            AbortToken,
            gate
        );

        await done.Task;
    }

    private static async Task _WaitUntilAsync(Func<bool> condition)
    {
        var deadline = TimeProvider.System.GetUtcNow() + _Timeout;

        while (!condition())
        {
            if (TimeProvider.System.GetUtcNow() >= deadline)
            {
                throw new TimeoutException("Condition was not met before the timeout.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(5), AbortToken);
        }
    }

    private sealed class CountingTimeProvider : TimeProvider
    {
        private long _timerCount;

        public long TimerCount => Interlocked.Read(ref _timerCount);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref _timerCount);
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }
}
