// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;

namespace Headless.Caching.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class FeatureCacheBenchmarks
{
    private const int _Seed = 8080;
    private readonly TimeSpan _expiration = TimeSpan.FromMilliseconds(500);
    private ICacheBenchmarkClient? _client;
    private string _key = "";
    private int _index;

    private BenchmarkPayload? PayloadValue { get; set; }

    [ParamsSource(typeof(BenchmarkScenarioSources), nameof(BenchmarkScenarioSources.FeatureProviders))]
    public string Provider { get; set; } = BenchmarkProviderIds.HeadlessInMemory;

    [Params(128, 4096)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        var prefix = BenchmarkKeyPrefix.Create(Provider, nameof(FeatureCacheBenchmarks), Guid.NewGuid().ToString("N"));
        _client = CacheBenchmarkClientFactory.Create(Provider, prefix);
        PayloadValue = BenchmarkPayloadFactory.Create(PayloadSize, _Seed);
        _key = "feature:hot";
        await Client
            .GetOrAddAsync(_key, _CreatePayloadAsync, _expiration, CancellationToken.None)
            .ConfigureAwait(false);
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_client is null)
        {
            return;
        }

        await _client.RemoveAsync(_key, CancellationToken.None).ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    [Benchmark(Baseline = true)]
    public async ValueTask<BenchmarkPayload?> FeatureHotGetOrAddAsync()
    {
        return await Client
            .GetOrAddAsync(_key, _CreatePayloadAsync, _expiration, CancellationToken.None)
            .ConfigureAwait(false);
    }

    [Benchmark]
    public async ValueTask<BenchmarkPayload?> FeatureColdGetOrAddAsync()
    {
        return await Client
            .GetOrAddAsync($"feature:cold:{_index++}", _CreatePayloadAsync, _expiration, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private ValueTask<BenchmarkPayload> _CreatePayloadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(Payload);
    }

    private ICacheBenchmarkClient Client =>
        _client ?? throw new InvalidOperationException("Benchmark client was not initialized.");

    private BenchmarkPayload Payload =>
        PayloadValue ?? throw new InvalidOperationException("Payload was not initialized.");
}
