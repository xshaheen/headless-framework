// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Checks;

namespace Headless.Caching;

/// <summary>
/// Routes raw-payload reads/writes through <see cref="IBufferCache"/> when the cache implements it, falling back
/// to the generic <c>byte[]</c> path on <see cref="ICache"/> otherwise. Lets a consumer take the
/// zero-intermediate-copy path without re-implementing the feature-detection at every call site.
/// </summary>
[PublicAPI]
public static class BufferCacheExtensions
{
    /// <summary>
    /// Reads the payload for <paramref name="key"/> into <paramref name="destination"/>, using the
    /// <see cref="IBufferCache"/> fast path when the cache supports it and the <c>byte[]</c> path otherwise.
    /// </summary>
    /// <returns><see langword="true"/> on a hit (payload written); <see langword="false"/> on miss or expiry.</returns>
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
    /// cache supports it and the <c>byte[]</c> path otherwise. The sequence is materialized synchronously before
    /// any await, so callers may hand in pooled buffers valid only for the duration of the call.
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

        // Fallback: materialize once before delegating — the byte[] path is all the generic ICache offers, and the
        // sequence may be pooled (valid only for this call), so the copy must happen before the first await.
        var bytes = value.ToArray();

        // UpsertEntryAsync reports insert-vs-update via a bool the raw write contract does not surface; drop it.
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
