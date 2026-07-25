// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;

namespace Headless.Caching.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class CacheEventDispatchBenchmarks : IAsyncDisposable
{
    private const int _OperationsPerInvoke = 1_024;
    private CacheEventsHub? _unobserved;
    private CacheEventsHub? _observed;
    private IDisposable? _subscription;

    [GlobalSetup]
    public void Setup()
    {
        var config = new CacheEventsConfig { BufferCapacity = _OperationsPerInvoke };
        _unobserved = new CacheEventsHub("benchmark", CacheTier.L1, config);
        _observed = new CacheEventsHub("benchmark", CacheTier.L1, config);
        _subscription = _observed.Hit.AddHandler(static _ => { });
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = _OperationsPerInvoke)]
    public void EmitUnobserved()
    {
        for (var i = 0; i < _OperationsPerInvoke; i++)
        {
            _unobserved!.OnHit("key", isStale: false);
        }
    }

    [Benchmark(OperationsPerInvoke = _OperationsPerInvoke)]
    public async ValueTask EmitObservedAndDrain()
    {
        for (var i = 0; i < _OperationsPerInvoke; i++)
        {
            _observed!.OnHit("key", isStale: false);
        }

        await _observed!.DrainAsync().ConfigureAwait(false);
    }

    [GlobalCleanup]
    public async ValueTask DisposeAsync()
    {
        _subscription?.Dispose();

        if (_observed is not null)
        {
            await _observed.DisposeAsync().ConfigureAwait(false);
        }

        if (_unobserved is not null)
        {
            await _unobserved.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}
