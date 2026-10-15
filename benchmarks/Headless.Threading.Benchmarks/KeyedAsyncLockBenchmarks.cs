// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace Headless.Threading.Benchmarks;

/// <summary>
/// Acquire-and-release cost of <see cref="KeyedAsyncLock"/> over a growing key set. A cold key is the common case:
/// the entry is removed when its reference count reaches zero, so every uncontended acquire creates it again.
/// </summary>
#pragma warning disable CA1001 // BenchmarkDotNet owns the lifecycle: IterationCleanup disposes the lock each iteration.
[MemoryDiagnoser]
public class KeyedAsyncLockBenchmarks
{
    // Total acquire/release pairs per invocation, split across the worker threads.
    private const int _TotalOperations = 200_000;

    private KeyedAsyncLock _lock = null!;
    private string[] _keys = null!;

    [Params(1, 1_000, 100_000)]
    public int KeyCount { get; set; }

    [Params(1, 16)]
    public int Threads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _keys = new string[KeyCount];

        for (var i = 0; i < _keys.Length; i++)
        {
            _keys[i] = string.Create(CultureInfo.InvariantCulture, $"resource:{i}");
        }
    }

    [IterationSetup]
    public void CreateLock()
    {
        _lock = new KeyedAsyncLock();
    }

    [IterationCleanup]
    public void DisposeLock()
    {
        _lock.Dispose();
    }

    [Benchmark(OperationsPerInvoke = _TotalOperations)]
    public Task LockAsyncAndDispose()
    {
        return _RunWorkersAsync(
            async (keyIndex) =>
            {
                using (await _lock.LockAsync(_keys[keyIndex]).ConfigureAwait(false)) { }
            }
        );
    }

    /// <summary>The timeout overload on a free key: the lock is available, so no wait or timer is needed.</summary>
    [Benchmark(OperationsPerInvoke = _TotalOperations)]
    public Task LockAsyncWithTimeoutOnFreeKey()
    {
        return _RunWorkersAsync(
            async (keyIndex) =>
            {
                using (
                    await _lock
                        .LockAsync(_keys[keyIndex], TimeSpan.FromSeconds(30), TimeProvider.System)
                        .ConfigureAwait(false)
                ) { }
            }
        );
    }

    private Task _RunWorkersAsync(Func<int, Task> operation)
    {
        var perWorker = _TotalOperations / Threads;
        var workers = new Task[Threads];

        for (var worker = 0; worker < workers.Length; worker++)
        {
            var offset = worker * perWorker;
            workers[worker] = Task.Run(async () =>
            {
                for (var i = 0; i < perWorker; i++)
                {
                    await operation((offset + i) % _keys.Length).ConfigureAwait(false);
                }
            });
        }

        return Task.WhenAll(workers);
    }
}
#pragma warning restore CA1001
