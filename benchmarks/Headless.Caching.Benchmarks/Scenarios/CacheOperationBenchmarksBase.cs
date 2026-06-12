// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;

namespace Headless.Caching.Benchmarks.Scenarios;

[MemoryDiagnoser]
public abstract class CacheOperationBenchmarksBase
{
    private const int Seed = 2112;
    private readonly TimeSpan _expiration = TimeSpan.FromMinutes(1);
    private ICacheBenchmarkClient? _client;
    private BenchmarkPayload? _payload;
    private string[] _keys = [];
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
        _payload = BenchmarkPayloadFactory.Create(PayloadSize, Seed);
        _keys = Enumerable.Range(0, KeyCardinality).Select(i => $"common:{i}").ToArray();

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
        return await Client.GetAsync(_NextKey(), CancellationToken.None).ConfigureAwait(false);
    }

    [Benchmark]
    public async ValueTask SetThenGetAsync()
    {
        var key = _NextKey();
        await Client.SetAsync(key, Payload, _expiration, CancellationToken.None).ConfigureAwait(false);
        await Client.GetAsync(key, CancellationToken.None).ConfigureAwait(false);
    }

    [Benchmark]
    public async ValueTask<BenchmarkPayload?> MissGetAsync()
    {
        return await Client.GetAsync($"missing:{_index++}", CancellationToken.None).ConfigureAwait(false);
    }

    [Benchmark]
    public async ValueTask RemoveAsync()
    {
        var key = $"remove:{_index++}";
        await Client.SetAsync(key, Payload, _expiration, CancellationToken.None).ConfigureAwait(false);
        await Client.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
    }

    private ICacheBenchmarkClient Client =>
        _client ?? throw new InvalidOperationException("Benchmark client was not initialized.");

    private BenchmarkPayload Payload => _payload ?? throw new InvalidOperationException("Payload was not initialized.");

    private string _NextKey()
    {
        var keys = _keys;
        var index = Math.Abs(_index++ % keys.Length);

        return keys[index];
    }
}
