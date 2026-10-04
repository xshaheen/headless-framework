// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Checks;

namespace Headless.Caching;

/// <summary>
/// Routes raw payload read and write operations through <see cref="IBufferCache"/> when the cache implements it,
/// or falls back to the generic <c>byte[]</c> path on <see cref="ICache"/>. Enables callers to use the
/// zero-copy path without repeating feature detection at each call site.
/// </summary>
[PublicAPI]
public static class BufferCacheExtensions
{
    /// <summary>
    /// Reads the payload for <paramref name="key"/> into <paramref name="destination"/>, using the
    /// <see cref="IBufferCache"/> fast path when the cache supports it and the <c>byte[]</c> path otherwise.
    /// </summary>
    /// <returns><see langword="true"/> when the payload is found and written to <paramref name="destination"/>; otherwise, <see langword="false"/>.</returns>
    public static ValueTask<bool> TryGetToOrFallbackAsync(
        this ICache cache,
        string key,
        IBufferWriter<byte> destination,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(cache);
        Argument.IsNotNull(destination);

        return cache is IBufferCache buffer
            ? buffer.TryGetToAsync(key, destination, cancellationToken)
            : _FallbackGetAsync(cache, key, destination, cancellationToken);
    }

    /// <summary>
    /// Upserts the payload from <paramref name="value"/>, using the <see cref="IBufferCache"/> fast path when the
    /// cache supports it or falling back to the <c>byte[]</c> path. The sequence is materialized synchronously before
    /// any asynchronous wait, which allows callers to supply pooled buffers valid only for the call duration.
    /// </summary>
    public static ValueTask UpsertRawOrFallbackAsync(
        this ICache cache,
        string key,
        ReadOnlySequence<byte> value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(cache);

        if (cache is IBufferCache buffer)
        {
            return buffer.UpsertRawAsync(key, value, options, cancellationToken);
        }

        // Materialize once before delegating. The byte[] path is all the generic ICache offers, and the
        // sequence might be pooled and valid only for this call, so the copy must occur before the first await.
        var bytes = value.ToArray();

        // UpsertEntryAsync reports insert versus update via a boolean that the raw write contract does not surface.
        return _DiscardResultAsync(cache.UpsertEntryAsync(key, bytes, options, cancellationToken));
    }

    private static async ValueTask _DiscardResultAsync(ValueTask<bool> pending)
    {
        await pending.ConfigureAwait(false);
    }

    private static async ValueTask<bool> _FallbackGetAsync(
        ICache cache,
        string key,
        IBufferWriter<byte> destination,
        CancellationToken cancellationToken
    )
    {
        var value = await cache.GetAsync<byte[]>(key, cancellationToken).ConfigureAwait(false);

        if (!value.HasValue || value.Value is null)
        {
            return false;
        }

        destination.Write(value.Value);

        return true;
    }
}
