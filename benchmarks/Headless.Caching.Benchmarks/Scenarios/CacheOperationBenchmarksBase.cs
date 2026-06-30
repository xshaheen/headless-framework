// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace Headless.Caching.Benchmarks.Scenarios;

[MemoryDiagnoser]
public abstract class CacheOperationBenchmarksBase
{
    private const int _Seed = 2112;
    private readonly TimeSpan _expiration = TimeSpan.FromMinutes(1);
    private ICacheBenchmarkClient? _client;
    private BenchmarkPayload? _payload;
    private string[] _keys = [];
    private string[] _missingKeys = [];
    private string[] _removeKeys = [];
    private int _index;

    protected abstract string ProviderId { get; }

    [Params(128, 4096)]
    public int PayloadSize { get; set; }

    [Params(1, 128)]
    public int KeyCardinality { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        var prefix = BenchmarkKeyPrefix.Create(ProviderId, GetType().Name, Guid.NewGuid().ToString("N"));
        _client = CacheBenchmarkClientFactory.Create(ProviderId, prefix);
        _payload = BenchmarkPayloadFactory.Create(PayloadSize, _Seed);
        _keys =
        [
            .. Enumerable
                .Range(0, KeyCardinality)
                .Select(i => string.Create(CultureInfo.InvariantCulture, $"common:{i}")),
        ];

        // Bounded working sets so the miss/remove loops measure steady-state op cost rather than accumulating an
        // unbounded key space across the millions of invocations BenchmarkDotNet runs per iteration.
        _missingKeys =
        [
            .. Enumerable
                .Range(0, KeyCardinality)
                .Select(i => string.Create(CultureInfo.InvariantCulture, $"missing:{i}")),
        ];
        _removeKeys =
        [
            .. Enumerable
                .Range(0, KeyCardinality)
                .Select(i => string.Create(CultureInfo.InvariantCulture, $"remove:{i}")),
        ];

        foreach (var key in _keys)
        {
            await _client.SetAsync(key, _payload, _expiration, CancellationToken.None).ConfigureAwait(false);
        }
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_client is null)
        {
            return;
        }

        foreach (var key in _keys)
        {
            await _client.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
        }

        await _client.DisposeAsync().ConfigureAwait(false);
    }

    [Benchmark(Baseline = true)]
    public async ValueTask<BenchmarkPayload?> HotGetAsync()
    {
        return await Client.GetAsync(_Next(_keys), CancellationToken.None).ConfigureAwait(false);
    }

    [Benchmark]
    public async ValueTask SetThenGetAsync()
    {
        var key = _Next(_keys);
        await Client.SetAsync(key, Payload, _expiration, CancellationToken.None).ConfigureAwait(false);
        await Client.GetAsync(key, CancellationToken.None).ConfigureAwait(false);
    }

    [Benchmark]
    public async ValueTask<BenchmarkPayload?> MissGetAsync()
    {
        return await Client.GetAsync(_Next(_missingKeys), CancellationToken.None).ConfigureAwait(false);
    }

    [Benchmark]
    public async ValueTask RemoveAsync()
    {
        var key = _Next(_removeKeys);
        await Client.SetAsync(key, Payload, _expiration, CancellationToken.None).ConfigureAwait(false);
        await Client.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
    }

    private ICacheBenchmarkClient Client =>
        _client ?? throw new InvalidOperationException("Benchmark client was not initialized.");

    private BenchmarkPayload Payload => _payload ?? throw new InvalidOperationException("Payload was not initialized.");

    private string _Next(string[] keys)
    {
        var index = Math.Abs(_index++ % keys.Length);

        return keys[index];
    }
}
