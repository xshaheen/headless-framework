// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace Headless.Caching.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class FactoryCacheBenchmarks
{
    private const int _Seed = 5050;
    private readonly TimeSpan _expiration = TimeSpan.FromMinutes(1);
    private ICacheBenchmarkClient? _client;
    private string _hotKey = "";
    private int _index;

    private BenchmarkPayload? PayloadValue { get; set; }

    [ParamsSource(typeof(BenchmarkScenarioSources), nameof(BenchmarkScenarioSources.GetOrAddProviders))]
    public string Provider { get; set; } = BenchmarkProviderIds.HeadlessInMemory;

    [Params(128, 4096)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        var prefix = BenchmarkKeyPrefix.Create(Provider, nameof(FactoryCacheBenchmarks), Guid.NewGuid().ToString("N"));
        _client = CacheBenchmarkClientFactory.Create(Provider, prefix);
        PayloadValue = BenchmarkPayloadFactory.Create(PayloadSize, _Seed);
        _hotKey = "factory:hot";

        await Client
            .GetOrAddAsync(_hotKey, _CreatePayloadAsync, _expiration, CancellationToken.None)
            .ConfigureAwait(false);
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_client is null)
        {
            return;
        }

        await _client.RemoveAsync(_hotKey, CancellationToken.None).ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    [Benchmark(Baseline = true)]
    public async ValueTask<BenchmarkPayload?> HotGetOrAddAsync()
    {
        return await Client
            .GetOrAddAsync(_hotKey, _CreatePayloadAsync, _expiration, CancellationToken.None)
            .ConfigureAwait(false);
    }

    [Benchmark]
    public async ValueTask<BenchmarkPayload?> ColdGetOrAddAsync()
    {
        return await Client
            .GetOrAddAsync(
                string.Create(CultureInfo.InvariantCulture, $"factory:cold:{_index++}"),
                _CreatePayloadAsync,
                _expiration,
                CancellationToken.None
            )
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
