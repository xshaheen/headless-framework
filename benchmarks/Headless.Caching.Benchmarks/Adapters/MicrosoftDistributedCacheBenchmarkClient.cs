// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Caching.Distributed;

namespace Headless.Caching.Benchmarks.Adapters;

internal sealed class MicrosoftDistributedCacheBenchmarkClient(
    CacheBenchmarkClientDescriptor descriptor,
    IDistributedCache cache
) : ICacheBenchmarkClient
{
    public CacheBenchmarkClientDescriptor Descriptor { get; } = descriptor;

    public async ValueTask SetAsync(
        string key,
        BenchmarkPayload value,
        TimeSpan expiration,
        CancellationToken cancellationToken
    )
    {
        await cache
            .SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value), _CreateOptions(expiration), cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<BenchmarkPayload?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var bytes = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);

        return bytes is null ? null : JsonSerializer.Deserialize<BenchmarkPayload>(bytes);
    }

    public async ValueTask<BenchmarkPayload?> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<BenchmarkPayload>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken
    )
    {
        var cached = await GetAsync(key, cancellationToken).ConfigureAwait(false);

        if (cached is not null)
        {
            return cached;
        }

        var value = await factory(cancellationToken).ConfigureAwait(false);
        await SetAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);

        return value;
    }

    public async ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (cache is IDisposable disposable)
        {
            disposable.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static DistributedCacheEntryOptions _CreateOptions(TimeSpan expiration) =>
        new() { AbsoluteExpirationRelativeToNow = expiration };
}
