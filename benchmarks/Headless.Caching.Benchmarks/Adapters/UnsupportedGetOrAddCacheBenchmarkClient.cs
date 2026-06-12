// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching.Benchmarks.Adapters;

internal abstract class UnsupportedGetOrAddCacheBenchmarkClient : ICacheBenchmarkClient
{
    public abstract CacheBenchmarkClientDescriptor Descriptor { get; }

    public abstract ValueTask SetAsync(
        string key,
        BenchmarkPayload value,
        TimeSpan expiration,
        CancellationToken cancellationToken
    );

    public abstract ValueTask<BenchmarkPayload?> GetAsync(string key, CancellationToken cancellationToken);

    public ValueTask<BenchmarkPayload?> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<BenchmarkPayload>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken
    )
    {
        throw new NotSupportedException($"{Descriptor.DisplayName} does not expose an atomic GetOrAdd operation.");
    }

    public abstract ValueTask RemoveAsync(string key, CancellationToken cancellationToken);

    public abstract ValueTask DisposeAsync();
}
