// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching.Benchmarks.Scenarios;

public static class BenchmarkScenarioSources
{
    public static IEnumerable<string> CommonProviders()
    {
        return CacheBenchmarkClientFactory
            .GetDescriptors(CacheBenchmarkClientFactory.IsRedisAvailable)
            .Select(x => x.Id);
    }

    public static IEnumerable<string> MemoryOnlyProviders()
    {
        return CacheBenchmarkClientFactory.MemoryOnlyProviderIds();
    }

    public static IEnumerable<string> DistributedOnlyProviders()
    {
        return CacheBenchmarkClientFactory.DistributedOnlyProviderIds(CacheBenchmarkClientFactory.IsRedisAvailable);
    }

    public static IEnumerable<string> GetOrAddProviders()
    {
        return CacheBenchmarkClientFactory
            .GetDescriptors(CacheBenchmarkClientFactory.IsRedisAvailable)
            .Where(x => x.Features.HasFlag(CacheBenchmarkFeatures.GetOrAdd))
            .Select(x => x.Id);
    }

    public static IEnumerable<string> FeatureProviders()
    {
        return CacheBenchmarkClientFactory
            .GetDescriptors(CacheBenchmarkClientFactory.IsRedisAvailable)
            .Where(x =>
                x.Features.HasFlag(CacheBenchmarkFeatures.FailSafe)
                || x.Features.HasFlag(CacheBenchmarkFeatures.EagerRefresh)
                || x.Features.HasFlag(CacheBenchmarkFeatures.Hybrid)
            )
            .Select(x => x.Id);
    }
}
