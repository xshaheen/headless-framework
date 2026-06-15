// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Headless.Caching;

/// <summary>Adapts a named Headless <see cref="ICache"/> instance to BCL <see cref="IDistributedCache"/>.</summary>
internal sealed class HeadlessDistributedCacheAdapter(
    ICache cache,
    IOptions<HeadlessDistributedCacheAdapterOptions> options,
    TimeProvider timeProvider
) : IDistributedCache
{
    private readonly HeadlessDistributedCacheAdapterOptions _options = Argument.IsNotNull(options).Value;

    /// <inheritdoc />
    public byte[]? Get(string key) => GetAsync(key).ConfigureAwait(false).GetAwaiter().GetResult();

    /// <inheritdoc />
    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        Argument.IsNotNullOrEmpty(key);

        var value = await cache.GetAsync<byte[]>(key, token).ConfigureAwait(false);

        return value.HasValue ? value.Value : null;
    }

    /// <inheritdoc />
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
        SetAsync(key, value, options).ConfigureAwait(false).GetAwaiter().GetResult();

    /// <inheritdoc />
    public async Task SetAsync(
        string key,
        byte[] value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(value);

        var mappedOptions = DistributedCacheEntryOptionsMapper.Map(
            options,
            _options.DefaultAbsoluteExpiration,
            timeProvider
        );

        await cache.UpsertEntryAsync(key, value, mappedOptions, token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Refresh(string key) => RefreshAsync(key).ConfigureAwait(false).GetAwaiter().GetResult();

    /// <inheritdoc />
    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        Argument.IsNotNullOrEmpty(key);

        await cache.RefreshAsync(key, token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Remove(string key) => RemoveAsync(key).ConfigureAwait(false).GetAwaiter().GetResult();

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        Argument.IsNotNullOrEmpty(key);

        await cache.RemoveAsync(key, token).ConfigureAwait(false);
    }
}
