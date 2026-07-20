// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Headless.Jobs.Enums;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class JobsTaskSchedulerTests : TestBase
{
    [Fact]
    public async Task queue_async_runs_user_work_on_thread_pool_without_a_synchronization_context()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool? startedOnThreadPool = null;
        SynchronizationContext? contextBeforeAwait = null;
        SynchronizationContext? contextAfterAwait = null;

        await scheduler.QueueAsync(
            async _ =>
            {
                startedOnThreadPool = Thread.CurrentThread.IsThreadPoolThread;
                contextBeforeAwait = SynchronizationContext.Current;
                await Task.Yield();
                contextAfterAwait = SynchronizationContext.Current;
                completed.TrySetResult();
            },
            JobPriority.Normal,
            AbortToken
        );

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        startedOnThreadPool.Should().BeTrue();
        contextBeforeAwait.Should().BeNull();
        contextAfterAwait.Should().BeNull();
    }

    [Fact]
    public async Task queue_async_does_not_throw_when_the_round_robin_index_wraps_past_int_max()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 2, timeProvider: TimeProvider.System);

        // Seed the round-robin counter just below int.MaxValue so the next enqueues cross the
        // int.MaxValue -> int.MinValue wrap. Before the sign-bit-mask fix, Math.Abs(int.MinValue) threw
        // OverflowException out of QueueAsync on exactly the enqueue that landed on int.MinValue.
        var indexField = typeof(JobsTaskScheduler).GetField(
            "_nextQueueIndex",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        indexField.Should().NotBeNull();
        indexField!.SetValue(scheduler, int.MaxValue - 1);

        var completed = 0;
        var enqueueAcrossWrap = async () =>
        {
            for (var i = 0; i < 4; i++)
            {
                await scheduler.QueueAsync(
                    _ =>
                    {
                        Interlocked.Increment(ref completed);
                        return Task.CompletedTask;
                    },
                    JobPriority.Normal,
                    AbortToken
                );
            }
        };

        await enqueueAcrossWrap.Should().NotThrowAsync();
        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        completed.Should().Be(4);
    }

    [Fact]
    public void worker_fault_restart_delay_grows_exponentially_then_caps()
    {
        var method = typeof(JobsTaskScheduler).GetMethod(
            "_GetWorkerFaultRestartDelay",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            [typeof(int)],
            modifiers: null
        );

        method.Should().NotBeNull();

        TimeSpan Delay(int consecutiveFaults) => (TimeSpan)method!.Invoke(null, [consecutiveFaults])!;

        // 100ms base, doubling per consecutive fault, so a fault storm slows itself down instead of spinning.
        Delay(1).Should().Be(TimeSpan.FromMilliseconds(100));
        Delay(2).Should().Be(TimeSpan.FromMilliseconds(200));
        Delay(3).Should().Be(TimeSpan.FromMilliseconds(400));
        Delay(4).Should().Be(TimeSpan.FromMilliseconds(800));

        // Capped at the 30s ceiling regardless of how high the fault count climbs (no overflow).
        Delay(20).Should().Be(TimeSpan.FromSeconds(30));
        Delay(int.MaxValue).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task queue_async_allows_nested_queueing_without_exceeding_concurrency()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        var nestedCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.QueueAsync(
            async _ =>
            {
                await scheduler.QueueAsync(
                    _ =>
                    {
                        nestedCompleted.TrySetResult();
                        return Task.CompletedTask;
                    },
                    JobPriority.Normal,
                    AbortToken
                );
            },
            JobPriority.Normal,
            AbortToken
        );

        await nestedCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    [Fact]
    public async Task queue_async_isolates_failed_work_from_the_next_item()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        var nextCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.QueueAsync(
            static _ => Task.FromException(new InvalidOperationException("Expected test failure.")),
            JobPriority.Normal,
            AbortToken
        );
        await scheduler.QueueAsync(
            _ =>
            {
                nextCompleted.TrySetResult();
                return Task.CompletedTask;
            },
            JobPriority.Normal,
            AbortToken
        );

        await nextCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    [Fact]
    public async Task long_running_work_keeps_its_dedicated_thread()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool? startedOnThreadPool = null;

        await scheduler.QueueAsync(
            _ =>
            {
                startedOnThreadPool = Thread.CurrentThread.IsThreadPoolThread;
                completed.TrySetResult();
                return Task.CompletedTask;
            },
            JobPriority.LongRunning,
            AbortToken
        );

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        startedOnThreadPool.Should().BeFalse();
    }

    [Fact]
    public async Task dispose_waits_for_active_long_running_work()
    {
        var scheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.QueueAsync(
            async _ =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);
            },
            JobPriority.LongRunning,
            AbortToken
        );

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        var dispose = scheduler.DisposeAsync().AsTask();

        (await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromMilliseconds(50), AbortToken))).Should().NotBe(dispose);

        release.TrySetResult();
        await dispose.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
    }

    [Fact]
    public async Task retired_worker_slots_restart_to_full_concurrency()
    {
        await using var scheduler = new JobsTaskScheduler(
            maxConcurrency: 3,
            timeProvider: TimeProvider.System,
            idleWorkerTimeout: TimeSpan.FromMilliseconds(25)
        );

        await _RunBlockedWaveAsync(scheduler, workerCount: 3);
        await _WaitUntilAsync(() => scheduler.ActiveWorkers <= 1, TimeSpan.FromSeconds(10));
        await _RunBlockedWaveAsync(scheduler, workerCount: 3);
    }

    [Fact]
    public async Task wait_for_running_tasks_async_waits_for_active_async_work()
    {
        await using var scheduler = new JobsTaskScheduler(
            maxConcurrency: 1,
            timeProvider: TimeProvider.System,
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
    public async Task queue_async_counts_active_async_work_against_max_concurrency()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 2, timeProvider: TimeProvider.System);
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
    public async Task queue_async_bounds_long_running_dedicated_threads()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 8, maxLongRunningConcurrency: 2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var twoStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedCount = 0;
        var activeCount = 0;
        var maxObservedActive = 0;

        async Task Work(CancellationToken _)
        {
            var active = Interlocked.Increment(ref activeCount);
            _SetMaxObserved(ref maxObservedActive, active);
            var started = Interlocked.Increment(ref startedCount);
            if (started == 2)
            {
                twoStarted.TrySetResult();
            }
            else if (started == 3)
            {
                allStarted.TrySetResult();
            }

            await release.Task.ConfigureAwait(false);
            Interlocked.Decrement(ref activeCount);
        }

        await scheduler.QueueAsync(Work, JobPriority.LongRunning, AbortToken);
        await scheduler.QueueAsync(Work, JobPriority.LongRunning, AbortToken);
        await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // The third admission parks on the detached lane; QueueAsync itself returns immediately.
        await scheduler.QueueAsync(Work, JobPriority.LongRunning, AbortToken);
        await Task.Delay(100, AbortToken);

        startedCount.Should().Be(2);
        maxObservedActive.Should().Be(2);

        release.SetResult();
        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();

        maxObservedActive.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task long_running_admission_honors_capacity_cancellation()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 2, maxLongRunningConcurrency: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.QueueAsync(
            async _ =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);
            },
            JobPriority.LongRunning,
            AbortToken
        );
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // A cancelled parked admission is dropped on the detached lane: the work must never run, and the slot it
        // was waiting for must remain claimable by later admissions (no leak, no ghost execution).
        var cancelledRan = false;
        using var capacityCts = new CancellationTokenSource();
        await scheduler.QueueAsync(
            _ =>
            {
                cancelledRan = true;
                return Task.CompletedTask;
            },
            JobPriority.LongRunning,
            capacityCts.Token
        );
        await capacityCts.CancelAsync();
        await Task.Delay(100, AbortToken);

        release.SetResult();
        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();

        var thirdRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await scheduler.QueueAsync(
            _ =>
            {
                thirdRan.TrySetResult();
                return Task.CompletedTask;
            },
            JobPriority.LongRunning,
            AbortToken
        );

        await thirdRan.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        cancelledRan.Should().BeFalse();
        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    [Fact]
    public async Task long_running_admission_releases_slot_after_work_faults()
    {
        // The dedicated-thread finally in _ExecuteLongRunningWorkAsync must release the permit even when the work
        // throws; otherwise a faulting long-running job would permanently leak its single slot.
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 2, maxLongRunningConcurrency: 1);
        var secondRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Occupy the only long-running slot with work that faults.
        await scheduler.QueueAsync(
            async _ =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            },
            JobPriority.LongRunning,
            AbortToken
        );

        // The fault is swallowed on the dedicated thread. The real release proof is below: if the finally had not
        // released the slot, the second admission would park forever and never run.
        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10)))
            .Should()
            .BeTrue();

        // If the slot had leaked, this second long-running admission would park forever.
        await scheduler.QueueAsync(
            _ =>
            {
                secondRan.TrySetResult();
                return Task.CompletedTask;
            },
            JobPriority.LongRunning,
            AbortToken
        );

        await secondRan.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    [Fact]
    public async Task dispose_drops_a_parked_long_running_admission()
    {
        // A second long-running admission parks in the detached admission lane when the only slot is occupied.
        // DisposeAsync cancels the shutdown token linked into that wait, so the parked admission must be dropped
        // (its work never runs) and disposal must complete rather than hang on the parked waiter.
        var scheduler = new JobsTaskScheduler(maxConcurrency: 2, maxLongRunningConcurrency: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Occupy the only long-running slot with a delegate that blocks until released.
        await scheduler.QueueAsync(
            async _ =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);
            },
            JobPriority.LongRunning,
            AbortToken
        );
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // With no slot free, this admission parks on the detached lane; QueueAsync returns immediately.
        var droppedRan = false;
        await scheduler.QueueAsync(
            _ =>
            {
                droppedRan = true;
                return Task.CompletedTask;
            },
            JobPriority.LongRunning,
            AbortToken
        );
        await Task.Delay(100, AbortToken);

        // Disposal cancels the shutdown token; the parked admission is dropped, never runs, and cannot block
        // disposal. Release the occupying delegate so the active-task drain can finish.
        var dispose = scheduler.DisposeAsync().AsTask();
        release.SetResult();
        await dispose.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        await Task.Delay(100, AbortToken);
        droppedRan.Should().BeFalse();
    }

    [Fact]
    public async Task saturated_long_running_admission_does_not_block_queueing_other_work()
    {
        // Regression for the head-of-line hazard: sequential dispatch loops await QueueAsync per batch item, so a
        // saturated long-running pool must not park the caller — ordinary-priority work queued after a blocked
        // long-running admission has to run while that admission is still waiting for a slot.
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 2, maxLongRunningConcurrency: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parkedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var normalRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Occupy the only long-running slot.
        await scheduler.QueueAsync(
            async _ =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);
            },
            JobPriority.LongRunning,
            AbortToken
        );
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // Pre-fix, this call parked the caller on the admission semaphore and the Normal job below was never
        // queued until the slot freed — the exact head-of-line shape of a sequential dispatch batch.
        await scheduler.QueueAsync(
            _ =>
            {
                parkedStarted.TrySetResult();
                return Task.CompletedTask;
            },
            JobPriority.LongRunning,
            AbortToken
        );

        await scheduler.QueueAsync(
            _ =>
            {
                normalRan.TrySetResult();
                return Task.CompletedTask;
            },
            JobPriority.Normal,
            AbortToken
        );

        // The Normal job runs while the second long-running admission is still parked behind the occupied slot.
        await normalRan.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        parkedStarted.Task.IsCompleted.Should().BeFalse();

        // Freeing the slot admits the parked work; everything drains.
        release.SetResult();
        await parkedStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    [Fact]
    public async Task saturated_long_running_admission_backlog_is_bounded()
    {
        // The detached admission lane must not accumulate waiters without bound: under sustained saturation the
        // fallback sweep re-dispatches still-parked jobs every lease cycle, so admissions beyond two per slot are
        // rejected outright and rely on the sweep's re-dispatch instead of parking another waiter.
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 2, maxLongRunningConcurrency: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executedCount = 0;

        // Occupy the only long-running slot.
        await scheduler.QueueAsync(
            async _ =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);
            },
            JobPriority.LongRunning,
            AbortToken
        );
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // Cap is two parked admissions per slot: the first two park, the remaining three are rejected outright.
        for (var i = 0; i < 5; i++)
        {
            await scheduler.QueueAsync(
                _ =>
                {
                    Interlocked.Increment(ref executedCount);
                    return Task.CompletedTask;
                },
                JobPriority.LongRunning,
                AbortToken
            );
        }

        release.SetResult();
        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        await Task.Delay(100, AbortToken);

        executedCount.Should().Be(2);
    }

    [Fact]
    public async Task queue_async_dispatches_high_then_normal_then_low_priority()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        var blockerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionOrder = new List<JobPriority>();

        await scheduler.QueueAsync(
            async _ =>
            {
                blockerStarted.TrySetResult();
                await releaseBlocker.Task.ConfigureAwait(false);
            },
            JobPriority.Normal,
            AbortToken
        );
        await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        foreach (var priority in new[] { JobPriority.Low, JobPriority.Normal, JobPriority.High })
        {
            await scheduler.QueueAsync(
                _ =>
                {
                    executionOrder.Add(priority);
                    return Task.CompletedTask;
                },
                priority,
                AbortToken
            );
        }

        releaseBlocker.SetResult();
        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();

        executionOrder.Should().Equal(JobPriority.High, JobPriority.Normal, JobPriority.Low);
    }

    [Fact]
    public async Task queue_async_steals_higher_priority_work_from_other_workers_before_local_low_priority()
    {
        var releaseWorkerOne = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scheduler = new JobsTaskScheduler(
            maxConcurrency: 2,
            timeProvider: TimeProvider.System,
            workerStartGate: (workerId, cancellationToken) =>
                workerId == 1 ? releaseWorkerOne.Task.WaitAsync(cancellationToken) : Task.CompletedTask
        );

        // Occupy both workers so the follow-up items stay parked in their queues until we release exactly one
        // worker. Worker 1 stays behind the start gate until worker 0 has taken the first blocker.
        var firstBlockerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstBlockerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondBlockerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondBlockerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.QueueAsync(
            async _ =>
            {
                firstBlockerStarted.TrySetResult();
                await firstBlockerRelease.Task.ConfigureAwait(false);
            },
            JobPriority.Normal,
            AbortToken
        );

        await firstBlockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        await scheduler.QueueAsync(
            async _ =>
            {
                secondBlockerStarted.TrySetResult();
                await secondBlockerRelease.Task.ConfigureAwait(false);
            },
            JobPriority.Normal,
            AbortToken
        );

        releaseWorkerOne.SetResult();
        await secondBlockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // With both workers parked, round-robin distribution places the single High item on worker 1's local
        // queue and leaves worker 0's local queue holding only a Low item. Releasing worker 0 forces its
        // dequeue to steal the High item from worker 1's queue before it runs its own local Low item.
        var executionOrder = new ConcurrentQueue<string>();
        var highRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        for (var i = 0; i < 2; i++)
        {
            await scheduler.QueueAsync(
                _ =>
                {
                    executionOrder.Enqueue("Low");
                    return Task.CompletedTask;
                },
                JobPriority.Low,
                AbortToken
            );
        }

        await scheduler.QueueAsync(
            _ =>
            {
                executionOrder.Enqueue("High");
                highRan.TrySetResult();
                return Task.CompletedTask;
            },
            JobPriority.High,
            AbortToken
        );

        // Release worker 0 only, keeping worker 1 parked, so worker 0 is the sole consumer and the drain
        // order is single-threaded and deterministic.
        firstBlockerRelease.SetResult();

        await highRan.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        executionOrder.TryPeek(out var firstExecuted).Should().BeTrue();
        firstExecuted.Should().Be("High");

        // Release the remaining worker and let everything drain.
        firstBlockerRelease.TrySetResult();
        secondBlockerRelease.TrySetResult();

        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();

        executionOrder.Should().Equal("High", "Low", "Low");
    }

    [Fact]
    public async Task queue_async_capacity_wait_honors_cancellation()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        var blockerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.QueueAsync(
            async _ =>
            {
                blockerStarted.TrySetResult();
                await releaseBlocker.Task.ConfigureAwait(false);
            },
            JobPriority.Normal,
            AbortToken
        );
        await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        for (var i = 0; i < 1024; i++)
        {
            await scheduler.QueueAsync(_ => Task.CompletedTask, JobPriority.Normal, AbortToken);
        }

        using var restartCts = new CancellationTokenSource();
        var capacityWait = scheduler.QueueAsync(_ => Task.CompletedTask, JobPriority.Normal, restartCts.Token);
        await restartCts.CancelAsync();

        var capacityWaitWasCancelled = false;
        try
        {
            await capacityWait;
        }
        catch (OperationCanceledException)
        {
            capacityWaitWasCancelled = true;
        }

        capacityWaitWasCancelled.Should().BeTrue();

        releaseBlocker.SetResult();
        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    [Fact]
    public async Task queue_async_capacity_cancellation_does_not_cancel_admitted_work()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        var blockerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admittedRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var capacityCts = new CancellationTokenSource();

        await scheduler.QueueAsync(
            async _ =>
            {
                blockerStarted.TrySetResult();
                await releaseBlocker.Task.ConfigureAwait(false);
            },
            JobPriority.Normal,
            AbortToken
        );
        await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        await scheduler.QueueAsync(
            _ =>
            {
                admittedRan.TrySetResult();
                return Task.CompletedTask;
            },
            JobPriority.Normal,
            capacityCts.Token,
            AbortToken
        );
        await capacityCts.CancelAsync();
        releaseBlocker.SetResult();

        await admittedRan.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
    }

    [Fact]
    public async Task dispose_while_worker_idles_in_backoff_completes_cleanly()
    {
        // A worker that finished its work parks in the idle backoff delay awaiting the shutdown
        // token. DisposeAsync cancels that token; the resulting OperationCanceledException must
        // not escape the worker's thread-start delegate — an unhandled exception on a manually
        // created thread terminates the whole process.
        var scheduler = new JobsTaskScheduler(maxConcurrency: 2, timeProvider: TimeProvider.System);
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

    [Fact]
    public async Task infrastructure_fault_restart_is_logged_and_rate_limited()
    {
        var timeProvider = new ToggleThrowingTimeProvider { ThrowOnGetUtcNow = true };
        var logger = Substitute.For<ILogger<JobsTaskScheduler>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var faultTimestamps = new ConcurrentQueue<long>();
        logger
            .When(log =>
                log.Log(
                    Arg.Any<LogLevel>(),
                    Arg.Any<EventId>(),
                    Arg.Any<object>(),
                    Arg.Any<Exception?>(),
                    Arg.Any<Func<object, Exception?, string>>()
                )
            )
            .Do(call =>
            {
                if (call.Arg<EventId>().Id == 3300)
                {
                    faultTimestamps.Enqueue(Stopwatch.GetTimestamp());
                }
            });
        await using var scheduler = new JobsTaskScheduler(
            maxConcurrency: 1,
            timeProvider: timeProvider,
            logger: logger
        );
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.QueueAsync(
            _ =>
            {
                completed.TrySetResult();
                return Task.CompletedTask;
            },
            JobPriority.Normal,
            AbortToken
        );

        await _WaitUntilAsync(() => faultTimestamps.Count >= 2, TimeSpan.FromSeconds(10));
        var timestamps = faultTimestamps.ToArray();
        Stopwatch
            .GetElapsedTime(timestamps[0], timestamps[1])
            .Should()
            .BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(80));

        timeProvider.ThrowOnGetUtcNow = false;
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
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

    private static async Task _RunBlockedWaveAsync(JobsTaskScheduler scheduler, int workerCount)
    {
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        for (var i = 0; i < workerCount; i++)
        {
            await scheduler.QueueAsync(
                async _ =>
                {
                    if (Interlocked.Increment(ref started) == workerCount)
                    {
                        allStarted.TrySetResult();
                    }

                    await release.Task.ConfigureAwait(false);
                },
                JobPriority.Normal,
                AbortToken
            );
        }

        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        scheduler.ActiveWorkers.Should().Be(workerCount);
        release.TrySetResult();
        (await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    private static async Task _WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = TimeProvider.System.GetUtcNow() + timeout;
        while (!condition())
        {
            if (TimeProvider.System.GetUtcNow() >= deadline)
            {
                throw new TimeoutException("Condition was not met before the timeout.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), AbortToken);
        }
    }

    private sealed class ToggleThrowingTimeProvider : TimeProvider
    {
        private int _throwOnGetUtcNow;

        public bool ThrowOnGetUtcNow
        {
            get => Volatile.Read(ref _throwOnGetUtcNow) != 0;
            set => Volatile.Write(ref _throwOnGetUtcNow, value ? 1 : 0);
        }

        public override DateTimeOffset GetUtcNow()
        {
            if (ThrowOnGetUtcNow)
            {
                throw new InvalidOperationException("Expected test clock failure.");
            }

            return base.GetUtcNow();
        }
    }
}
