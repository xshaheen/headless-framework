// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching.Benchmarks.Adapters;

internal sealed class HeadlessCacheBenchmarkClient(
    CacheBenchmarkClientDescriptor descriptor,
    ICache cache,
    params object[] ownedResources
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
        await cache.UpsertAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<BenchmarkPayload?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync<BenchmarkPayload>(key, cancellationToken).ConfigureAwait(false);

        return cached.HasValue ? cached.Value : null;
    }

    public async ValueTask<BenchmarkPayload?> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<BenchmarkPayload>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken
    )
    {
        var cached = await cache
            .GetOrAddAsync(
                key,
                async ct => await factory(ct).ConfigureAwait(false),
                new CacheEntryOptions { Duration = expiration },
                cancellationToken
            )
            .ConfigureAwait(false);

        return cached.HasValue ? cached.Value : null;
    }

    public async ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (cache is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (cache is IDisposable disposable)
        {
            disposable.Dispose();
        }

        foreach (var resource in ownedResources)
        {
            switch (resource)
            {
                case IAsyncDisposable ownedAsyncDisposable:
                    await ownedAsyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;

                case IDisposable ownedDisposable:
                    ownedDisposable.Dispose();
                    break;
            }
        }
    }
}
