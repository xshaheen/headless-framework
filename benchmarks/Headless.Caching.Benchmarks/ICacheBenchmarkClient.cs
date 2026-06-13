// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching.Benchmarks;

internal interface ICacheBenchmarkClient : IAsyncDisposable
{
    CacheBenchmarkClientDescriptor Descriptor { get; }

    ValueTask SetAsync(string key, BenchmarkPayload value, TimeSpan expiration, CancellationToken cancellationToken);

    ValueTask<BenchmarkPayload?> GetAsync(string key, CancellationToken cancellationToken);

    ValueTask<BenchmarkPayload?> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<BenchmarkPayload>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken
    );

    ValueTask RemoveAsync(string key, CancellationToken cancellationToken);
}
