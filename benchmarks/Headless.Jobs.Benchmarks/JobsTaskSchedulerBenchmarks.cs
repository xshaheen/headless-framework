// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using Headless.Jobs.Enums;
using Headless.Jobs.JobsThreadPool;

namespace Headless.Jobs.Benchmarks;

[MemoryDiagnoser]
public class JobsTaskSchedulerBenchmarks
{
    private const int _WorkItemCount = 256;

    [Benchmark(OperationsPerInvoke = _WorkItemCount, Description = "Queue and drain completed work")]
    public async Task QueueAndDrainCompletedWork()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 4);

        for (var i = 0; i < _WorkItemCount; i++)
        {
            await scheduler.QueueAsync(static _ => Task.CompletedTask, JobPriority.Normal).ConfigureAwait(false);
        }

        await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }

    [Benchmark(OperationsPerInvoke = _WorkItemCount, Description = "Queue and drain yielding work")]
    public async Task QueueAndDrainYieldingWork()
    {
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 4);

        for (var i = 0; i < _WorkItemCount; i++)
        {
            await scheduler.QueueAsync(_YieldOnceAsync, JobPriority.Normal).ConfigureAwait(false);
        }

        await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }

    [Benchmark(Description = "Dispose idle scheduler")]
    public async Task DisposeIdleScheduler()
    {
        var scheduler = new JobsTaskScheduler(maxConcurrency: 4);
        await scheduler.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task _YieldOnceAsync(CancellationToken _)
    {
        await Task.Yield();
    }

    internal static async Task RunLatencyProbeAsync()
    {
        const int enqueueSampleCount = 2_000;
        const int shutdownSampleCount = 100;
        var enqueueSamples = new double[enqueueSampleCount];

        await using (var scheduler = new JobsTaskScheduler(maxConcurrency: 4))
        {
            for (var i = 0; i < 32; i++)
            {
                await scheduler.QueueAsync(static _ => Task.CompletedTask, JobPriority.Normal).ConfigureAwait(false);
            }

            await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            for (var i = 0; i < enqueueSamples.Length; i++)
            {
                var started = Stopwatch.GetTimestamp();
                await scheduler.QueueAsync(static _ => Task.CompletedTask, JobPriority.Normal).ConfigureAwait(false);
                enqueueSamples[i] = Stopwatch.GetElapsedTime(started).TotalMicroseconds;
            }

            await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        var shutdownSamples = new double[shutdownSampleCount];
        for (var i = 0; i < shutdownSamples.Length; i++)
        {
            var scheduler = new JobsTaskScheduler(maxConcurrency: 4);
            var started = Stopwatch.GetTimestamp();
            await scheduler.DisposeAsync().ConfigureAwait(false);
            shutdownSamples[i] = Stopwatch.GetElapsedTime(started).TotalMicroseconds;
        }

        Console.WriteLine(_FormatPercentiles("enqueue_us", enqueueSamples));
        Console.WriteLine(_FormatPercentiles("shutdown_us", shutdownSamples));
    }

    private static string _FormatPercentiles(string name, double[] samples)
    {
        Array.Sort(samples);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name}: p50={_Percentile(samples, 0.50):F3}, p95={_Percentile(samples, 0.95):F3}, "
                + $"p99={_Percentile(samples, 0.99):F3}, max={samples[^1]:F3}, n={samples.Length}"
        );
    }

    private static double _Percentile(double[] sortedSamples, double percentile)
    {
        var index = (int)Math.Ceiling(sortedSamples.Length * percentile) - 1;
        return sortedSamples[Math.Clamp(index, 0, sortedSamples.Length - 1)];
    }
}
