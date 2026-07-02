// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Foundatio.Caching;

namespace Headless.Caching.Benchmarks.Adapters;

internal sealed class FoundatioCacheBenchmarkClient(
    CacheBenchmarkClientDescriptor descriptor,
    ICacheClient cache,
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
        await cache.SetAsync(key, value, expiration).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<BenchmarkPayload?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync<BenchmarkPayload>(key).WaitAsync(cancellationToken).ConfigureAwait(false);

        return cached.HasValue ? cached.Value : null;
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
        await cache.RemoveAsync(key).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        switch (cache)
        {
            // ReSharper disable once SuspiciousTypeConversion.Global
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);

                break;
            case IDisposable disposable:
                disposable.Dispose();

                break;
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
