// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Threading.Channels;

#pragma warning disable CA1303, MA0076 // Console report of a standalone scenario: not localized, and the invariant-culture numbers are only read by a person.

namespace Headless.Messaging.Benchmarks.Scenarios;

/// <summary>
/// Compares the dispatcher's parallel send loop, which reads a batch and awaits all of it before reading the next,
/// with a sliding window of the same width, against a transport where 1% of sends take 200 ms. Wall-clock throughput
/// and tail latency do not fit BenchmarkDotNet's per-invocation model, so this runs as a standalone scenario:
/// <c>dotnet run -c Release --project benchmarks/Headless.Messaging.Benchmarks -- send-window</c>.
/// </summary>
internal static class ParallelSendWindowScenario
{
    private const int _FastSendMs = 1;
    private const int _SlowSendMs = 200;
    private const int _SlowEvery = 100;

    private enum Strategy
    {
        BatchBarrier,
        SemaphoreWindow,
        ParallelForEach,
    }

    public static async Task RunAsync()
    {
        Console.WriteLine(
            $"{"strategy", -16} {"width", 5} {"mode", -12} {"msgs", 6} {"msgs/s", 9} {"p50 ms", 8} {"p95 ms", 8} {"p99 ms", 8} {"max ms", 8}"
        );

        foreach (var width in new[] { 10, 50, 200 })
        {
            foreach (var strategy in Enum.GetValues<Strategy>())
            {
                // Backlog: everything is queued up front, so msgs/s is the loop's own capacity.
                await _RunAsync(strategy, width, messages: 6000, offeredPerSecond: null);

                // Open loop: arrivals at a fixed rate below the barrier's capacity, so latency is the queueing delay.
                await _RunAsync(strategy, width, messages: 2000, offeredPerSecond: 400);
            }
        }
    }

    private static async Task _RunAsync(Strategy strategy, int width, int messages, int? offeredPerSecond)
    {
        var channel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions { SingleReader = true });
        var latencies = new double[messages];
        var completed = 0;
        var clock = Stopwatch.StartNew();
        var sent = 0;

        async Task SendAsync(long enqueuedAt)
        {
            var index = Interlocked.Increment(ref sent) - 1;
            await Task.Delay(index % _SlowEvery == _SlowEvery - 1 ? _SlowSendMs : _FastSendMs).ConfigureAwait(false);
            latencies[Interlocked.Increment(ref completed) - 1] = Stopwatch
                .GetElapsedTime(enqueuedAt)
                .TotalMilliseconds;
        }

        var consumer = strategy switch
        {
            Strategy.BatchBarrier => _BarrierAsync(channel.Reader, width, SendAsync),
            Strategy.SemaphoreWindow => _SemaphoreAsync(channel.Reader, width, SendAsync),
            _ => _ParallelForEachAsync(channel.Reader, width, SendAsync),
        };

        var interval = offeredPerSecond is { } rate ? TimeSpan.FromSeconds(1d / rate) : TimeSpan.Zero;
        var next = Stopwatch.GetTimestamp();
        for (var i = 0; i < messages; i++)
        {
            if (offeredPerSecond is not null)
            {
                next += (long)(interval.TotalSeconds * Stopwatch.Frequency);
                var wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), next);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait).ConfigureAwait(false);
                }
            }

            channel.Writer.TryWrite(Stopwatch.GetTimestamp());
        }

        channel.Writer.Complete();
        await consumer.ConfigureAwait(false);
        clock.Stop();

        Array.Sort(latencies);
        Console.WriteLine(
            $"{strategy, -16} {width, 5} {(offeredPerSecond is null ? "backlog" : $"open {offeredPerSecond}/s"), -12} {messages, 6} "
                + $"{messages / clock.Elapsed.TotalSeconds, 9:F0} {latencies[messages / 2], 8:F1} "
                + $"{latencies[(int)(messages * 0.95)], 8:F1} {latencies[(int)(messages * 0.99)], 8:F1} {latencies[^1], 8:F1}"
        );
    }

    // Mirrors Dispatcher._SendBatchParallelAsync: wait for work, take up to a batch, await the whole batch.
    private static async Task _BarrierAsync(ChannelReader<long> reader, int width, Func<long, Task> send)
    {
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            var tasks = new List<Task>(width);
            for (var i = 0; i < width && reader.TryRead(out var item); i++)
            {
                tasks.Add(send(item));
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }
    }

    private static async Task _SemaphoreAsync(ChannelReader<long> reader, int width, Func<long, Task> send)
    {
        using var slots = new SemaphoreSlim(width, width);
        var inFlight = new List<Task>();

        await foreach (var item in reader.ReadAllAsync().ConfigureAwait(false))
        {
            await slots.WaitAsync().ConfigureAwait(false);
            var task = Task.Run(async () =>
            {
                try
                {
                    await send(item).ConfigureAwait(false);
                }
                finally
                {
                    slots.Release();
                }
            });

            lock (inFlight)
            {
                inFlight.RemoveAll(static t => t.IsCompleted);
                inFlight.Add(task);
            }
        }

        await Task.WhenAll(inFlight).ConfigureAwait(false);
    }

    private static Task _ParallelForEachAsync(ChannelReader<long> reader, int width, Func<long, Task> send) =>
        Parallel.ForEachAsync(
            reader.ReadAllAsync(),
            new ParallelOptions { MaxDegreeOfParallelism = width },
            async (item, _) => await send(item).ConfigureAwait(false)
        );
}
