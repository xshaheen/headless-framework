// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Jobs.JobsThreadPool;

namespace Headless.Jobs.Benchmarks;

/// <summary>
/// Counting probes for the worker pool. Wake-ups, head-of-line blocking, and thread creation are counts and
/// completion times of one run, not per-operation throughput, so they run outside BenchmarkDotNet
/// (<c>--worker-pool-probe</c>) like the latency probe.
/// </summary>
internal static class JobsWorkerPoolProbe
{
    public static async Task RunAsync()
    {
        await _RunIdleWakeUpsAsync().ConfigureAwait(false);
        await _RunMixedFunctionFanOutAsync().ConfigureAwait(false);
        await _RunLongRunningThreadsAsync().ConfigureAwait(false);
    }

    // An idle host has no jobs: every timer the scheduler arms is a wake-up that did nothing.
    private static async Task _RunIdleWakeUpsAsync()
    {
        const int windowSeconds = 5;

        foreach (var maxConcurrency in new[] { 1, 4, 16 })
        {
            var timeProvider = new CountingTimeProvider();
            await using var scheduler = new JobsTaskScheduler(maxConcurrency, timeProvider);

            // One job spawns the worker set the host normally has after its first burst.
            await scheduler.QueueAsync(static _ => Task.CompletedTask, JobPriority.Normal).ConfigureAwait(false);
            await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(1), TimeProvider.System).ConfigureAwait(false); // settle past the spin phase

            var before = timeProvider.TimerCount;
            var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            await Task.Delay(TimeSpan.FromSeconds(windowSeconds), TimeProvider.System).ConfigureAwait(false);
            var wakeUps = timeProvider.TimerCount - before;
            var cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalMilliseconds;

            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"idle_wakeups maxConcurrency={maxConcurrency} window={windowSeconds}s workers={scheduler.ActiveWorkers} timer_wakeups={wakeUps} per_second={wakeUps / (double)windowSeconds:F1} cpu_ms={cpuMs:F1}"
                )
            );
        }
    }

    // One function capped at 1 gets N jobs, then N uncapped jobs of another function arrive. Jobs hold their slot
    // for workMs. Reports when the uncapped set completes, with the gate waited inside the work item (the shape that
    // parks a worker per queued capped job) and handed to the scheduler (which parks the item instead).
    private static async Task _RunMixedFunctionFanOutAsync()
    {
        const int jobsPerFunction = 32;
        const int workMs = 20;

        foreach (var gateInScheduler in new[] { false, true })
        {
            foreach (var maxConcurrency in new[] { 4, 8 })
            {
                await using var scheduler = new JobsTaskScheduler(maxConcurrency, TimeProvider.System);
                using var capGate = new SemaphoreSlim(1, 1);
                using var uncappedDone = new CountdownEvent(jobsPerFunction);
                using var cappedDone = new CountdownEvent(jobsPerFunction);
                var started = Stopwatch.GetTimestamp();

                for (var i = 0; i < jobsPerFunction; i++)
                {
                    Func<CancellationToken, Task> capped = gateInScheduler
                        ? async ct =>
                        {
                            await Task.Delay(workMs, ct).ConfigureAwait(false);
                            cappedDone.Signal();
                        }
                        : async ct =>
                        {
                            await capGate.WaitAsync(ct).ConfigureAwait(false);
                            try
                            {
                                await Task.Delay(workMs, ct).ConfigureAwait(false);
                            }
                            finally
                            {
                                capGate.Release();
                                cappedDone.Signal();
                            }
                        };

                    await scheduler
                        .QueueAsync(
                            capped,
                            JobPriority.Normal,
                            CancellationToken.None,
                            CancellationToken.None,
                            gateInScheduler ? capGate : null
                        )
                        .ConfigureAwait(false);
                }

                for (var i = 0; i < jobsPerFunction; i++)
                {
                    await scheduler
                        .QueueAsync(
                            async ct =>
                            {
                                await Task.Delay(workMs, ct).ConfigureAwait(false);
                                uncappedDone.Signal();
                            },
                            JobPriority.Normal
                        )
                        .ConfigureAwait(false);
                }

                await _WaitAsync(uncappedDone).ConfigureAwait(false);
                var uncappedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                await _WaitAsync(cappedDone).ConfigureAwait(false);
                var cappedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

                Console.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"mixed_fanout gate={(gateInScheduler ? "scheduler" : "in_work_item")} maxConcurrency={maxConcurrency} jobsPerFunction={jobsPerFunction} workMs={workMs} uncapped_done_ms={uncappedMs:F0} capped_done_ms={cappedMs:F0} uncapped_alone_ideal_ms={Math.Ceiling(jobsPerFunction / (double)maxConcurrency) * workMs:F0}"
                    )
                );
            }
        }
    }

    // LongRunning wraps an async delegate: the dedicated thread is released at the first await. Counts handlers
    // that started on a non-pool thread (each one a created thread) against the same handlers on the shared pool.
    private static async Task _RunLongRunningThreadsAsync()
    {
        const int jobCount = 64;

        foreach (var priority in new[] { JobPriority.LongRunning, JobPriority.Normal })
        {
            await using var scheduler = new JobsTaskScheduler(
                maxConcurrency: 16,
                TimeProvider.System,
                maxLongRunningConcurrency: jobCount
            );
            var dedicatedThreadIds = new ConcurrentDictionary<int, byte>();
            using var done = new CountdownEvent(jobCount);
            var processThreadsBefore = Process.GetCurrentProcess().Threads.Count;
            var started = Stopwatch.GetTimestamp();

            for (var i = 0; i < jobCount; i++)
            {
                await scheduler
                    .QueueAsync(
                        async ct =>
                        {
                            if (!Thread.CurrentThread.IsThreadPoolThread)
                            {
                                dedicatedThreadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
                            }

                            await Task.Delay(50, ct).ConfigureAwait(false);
                            done.Signal();
                        },
                        priority
                    )
                    .ConfigureAwait(false);
            }

            await _WaitAsync(done).ConfigureAwait(false);
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"long_running_threads priority={priority} jobs={jobCount} dedicated_threads_created={dedicatedThreadIds.Count} process_threads_delta_at_end={Process.GetCurrentProcess().Threads.Count - processThreadsBefore} elapsed_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}"
                )
            );
        }
    }

    private static Task<bool> _WaitAsync(CountdownEvent countdown)
    {
        return Task.Run(() => countdown.Wait(TimeSpan.FromSeconds(60)));
    }

    // Delay, WaitAsync, and CancelAfter all arm a timer through CreateTimer, so the count is every timed wake-up.
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
