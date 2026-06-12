// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Caching.Benchmarks;
using Headless.Caching.Benchmarks.Scenarios;

BenchmarkSwitcher
    .FromTypes([
        typeof(CommonCacheBenchmarks),
        typeof(MemoryOnlyCacheBenchmarks),
        typeof(DistributedOnlyCacheBenchmarks),
        typeof(FactoryCacheBenchmarks),
        typeof(FeatureCacheBenchmarks),
    ])
    .Run(args, BenchmarkRunConfig.Create());
