// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;

namespace Headless.Caching.Benchmarks.Scenarios;

public class DistributedOnlyCacheBenchmarks : CacheOperationBenchmarksBase
{
    [ParamsSource(typeof(BenchmarkScenarioSources), nameof(BenchmarkScenarioSources.DistributedOnlyProviders))]
    public string Provider { get; set; } = BenchmarkProviderIds.MicrosoftMemoryDistributed;

    protected override string ProviderId => Provider;
}
