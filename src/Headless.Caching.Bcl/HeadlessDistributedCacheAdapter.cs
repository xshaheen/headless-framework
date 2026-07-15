// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Checks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Headless.Caching;

/// <summary>
/// Adapts a named Headless <see cref="ICache"/> instance to BCL <see cref="IDistributedCache"/> and its
/// buffer-oriented extension <see cref="IBufferDistributedCache"/>. The buffer members route through
/// <see cref="IBufferCache"/> (Redis/InMemory/Hybrid) when the named cache supports it and fall back to the
/// generic <c>byte[]</c> path otherwise.
/// </summary>
/// <remarks>
/// The synchronous BCL members (<c>Get</c>, <c>Set</c>, <c>Refresh</c>, <c>Remove</c>, <c>TryGet</c>) block
/// on their async counterparts via <c>GetAwaiter().GetResult()</c>. This is safe for all current
/// <see cref="ICache"/> implementations because they use <c>ConfigureAwait(false)</c> throughout and do not
/// capture a synchronization context. A future <see cref="ICache"/> implementation that captures the ambient
/// context could deadlock these synchronous members; prefer the async overloads when possible.
/// </remarks>
internal sealed class HeadlessDistributedCacheAdapter(
    ICache cache,
    IOptions<HeadlessDistributedCacheAdapterOptions> options,
    TimeProvider timeProvider
) : IBufferDistributedCache
{
    private readonly HeadlessDistributedCacheAdapterOptions _options = Argument.IsNotNull(options).Value;

    /// <inheritdoc />
    public byte[]? Get(string key)
    {
        return GetAsync(key).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        Argument.IsNotNullOrEmpty(key);

        var value = await cache.GetAsync<byte[]>(key, token).ConfigureAwait(false);

        return value.HasValue ? value.Value : null;
    }

    /// <inheritdoc />
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        SetAsync(key, value, options).ConfigureAwait(false).GetAwaiter().GetResult();
    }

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
    public bool TryGet(string key, IBufferWriter<byte> destination)
    {
        return TryGetAsync(key, destination).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public ValueTask<bool> TryGetAsync(string key, IBufferWriter<byte> destination, CancellationToken token = default)
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(destination);

        // When the named cache implements IBufferCache the payload is written straight into destination with no
        // intermediate byte[]; otherwise the helper falls back to GetAsync<byte[]> + a single copy. IBufferWriter
        // has no flush concept (the helper writes via Write/Advance), so the BCL consumer reads the written
        // segments directly — no flush bridging needed unlike the PipeWriter output-cache path.
        return cache.TryGetToOrFallbackAsync(key, destination, token);
    }

    /// <inheritdoc />
    public void Set(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options)
    {
        SetAsync(key, value, options).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public ValueTask SetAsync(
        string key,
        ReadOnlySequence<byte> value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default
    )
    {
        Argument.IsNotNullOrEmpty(key);

        var mappedOptions = DistributedCacheEntryOptionsMapper.Map(
            options,
            _options.DefaultAbsoluteExpiration,
            timeProvider
        );

        // The value sequence may be pooled (valid only for this call); UpsertRawOrFallbackAsync consumes it
        // synchronously (the IBufferCache raw fast path frames it without an intermediate byte[]; the fallback
        // copies it once). Invoke the helper here in the synchronous body, before this method returns, then await
        // only the resulting task.
        return cache.UpsertRawOrFallbackAsync(key, value, mappedOptions, token);
    }

    /// <inheritdoc />
    public void Refresh(string key)
    {
        RefreshAsync(key).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        Argument.IsNotNullOrEmpty(key);

        await cache.RefreshAsync(key, token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        RemoveAsync(key).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        Argument.IsNotNullOrEmpty(key);

        await cache.RemoveAsync(key, token).ConfigureAwait(false);
    }
}
