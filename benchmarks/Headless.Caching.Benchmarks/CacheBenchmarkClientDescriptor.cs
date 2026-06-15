// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching.Benchmarks;

internal sealed record CacheBenchmarkClientDescriptor(
    string Id,
    string DisplayName,
    CacheBenchmarkBackend Backend,
    CacheBenchmarkFeatures Features
);
