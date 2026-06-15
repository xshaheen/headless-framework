// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;

namespace Headless.Caching.Benchmarks.Scenarios;

public class CommonCacheBenchmarks : CacheOperationBenchmarksBase
{
    [ParamsSource(typeof(BenchmarkScenarioSources), nameof(BenchmarkScenarioSources.CommonProviders))]
    public string Provider { get; set; } = BenchmarkProviderIds.HeadlessInMemory;

    protected override string ProviderId => Provider;
}
