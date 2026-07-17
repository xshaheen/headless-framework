// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Caching.Memory;

namespace Headless.Caching.Benchmarks.Adapters;

internal sealed class MicrosoftMemoryCacheBenchmarkClient(
    CacheBenchmarkClientDescriptor descriptor,
    IMemoryCache cache,
    string keyPrefix
) : ICacheBenchmarkClient
{
    public CacheBenchmarkClientDescriptor Descriptor { get; } = descriptor;

    public ValueTask SetAsync(
        string key,
        BenchmarkPayload value,
        TimeSpan expiration,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        cache.Set(_GetKey(key), value, expiration);

        return ValueTask.CompletedTask;
    }

    public ValueTask<BenchmarkPayload?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(cache.TryGetValue<BenchmarkPayload>(_GetKey(key), out var value) ? value : null);
    }

    public async ValueTask<BenchmarkPayload?> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<BenchmarkPayload>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken
    )
    {
        var prefixedKey = _GetKey(key);

        if (cache.TryGetValue<BenchmarkPayload>(prefixedKey, out var cached))
        {
            return cached;
        }

        var value = await factory(cancellationToken).ConfigureAwait(false);
        cache.Set(prefixedKey, value, expiration);

        return value;
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        cache.Remove(_GetKey(key));

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        cache.Dispose();

        return ValueTask.CompletedTask;
    }

    private string _GetKey(string key)
    {
        return keyPrefix + key;
    }
}
