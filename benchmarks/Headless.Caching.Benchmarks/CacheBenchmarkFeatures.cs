// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching.Benchmarks;

[Flags]
internal enum CacheBenchmarkFeatures
{
    None = 0,
    GetOrAdd = 1 << 0,
    Hybrid = 1 << 1,
    FailSafe = 1 << 2,
    EagerRefresh = 1 << 3,
}
