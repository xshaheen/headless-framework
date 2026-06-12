// Copyright (c) Mahmoud Shaheen. All rights reserved.

using ZiggyCreatures.Caching.Fusion;

namespace Headless.Caching.Benchmarks.Adapters;

internal sealed class FusionCacheBenchmarkClient(
    CacheBenchmarkClientDescriptor descriptor,
    IFusionCache cache,
    IServiceProvider services
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
            .SetAsync(key, value, _CreateOptions(expiration), tags: null, token: cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<BenchmarkPayload?> GetAsync(string key, CancellationToken cancellationToken)
    {
        return await cache
            .GetOrDefaultAsync<BenchmarkPayload?>(key, null, token: cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<BenchmarkPayload?> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<BenchmarkPayload>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken
    )
    {
        return await cache
            .GetOrSetAsync<BenchmarkPayload>(
                key,
                async (_, ct) => await factory(ct).ConfigureAwait(false),
                MaybeValue<BenchmarkPayload>.None,
                options: _CreateOptions(expiration),
                tags: null,
                token: cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(key, token: cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (services is IDisposable disposable)
        {
            disposable.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static FusionCacheEntryOptions _CreateOptions(TimeSpan expiration) =>
        new FusionCacheEntryOptions(expiration)
            .SetFailSafe(true, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1))
            .SetEagerRefresh(0.8f);
}
