// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;

namespace Headless.Caching.Benchmarks.Scenarios;

public class MemoryOnlyCacheBenchmarks : CacheOperationBenchmarksBase
{
    [ParamsSource(typeof(BenchmarkScenarioSources), nameof(BenchmarkScenarioSources.MemoryOnlyProviders))]
    public string Provider { get; set; } = BenchmarkProviderIds.HeadlessInMemory;

    protected override string ProviderId => Provider;
}
