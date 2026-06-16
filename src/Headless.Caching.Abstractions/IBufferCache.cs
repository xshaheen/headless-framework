// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;

namespace Headless.Caching;

/// <summary>
/// Capability interface for caches that can read and write a raw payload without materializing an intermediate
/// <see cref="byte"/> array. Byte-oriented providers (Redis, InMemory, Hybrid) implement this alongside
/// <see cref="ICache"/>; consumers holding opaque byte payloads (the ASP.NET Core output-cache and BCL
/// distributed-cache adapters) feature-detect it via <see cref="BufferCacheExtensions"/> and fall back to the
/// generic <c>byte[]</c> path on <see cref="ICache"/> when a provider does not implement it.
/// </summary>
[PublicAPI]
public interface IBufferCache
{
    /// <summary>
    /// Reads the payload for <paramref name="key"/> directly into <paramref name="destination"/> without
    /// materializing a standalone <see cref="byte"/> array, honoring the same logical-expiry and tag-invalidation
    /// semantics as <see cref="ICache.GetAsync{T}"/>. Nothing is written on a miss.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="destination">The buffer writer the payload is written into (for example a <c>PipeWriter</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> on a hit (payload written); <see langword="false"/> on miss or expiry.</returns>
    ValueTask<bool> TryGetToAsync(
        string key,
        IBufferWriter<byte> destination,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Upserts the payload from <paramref name="value"/> without materializing a standalone <see cref="byte"/>
    /// array, stamping the entry with the full <see cref="CacheEntryOptions"/> semantics (CreatedAt, tags,
    /// fail-safe, sliding) exactly like <see cref="ICache.UpsertEntryAsync{T}"/>. The sequence is consumed before
    /// the first await, so callers may hand in pooled buffers valid only for the duration of the call.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The raw payload to persist.</param>
    /// <param name="options">The cache entry options applied to the written entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown when <see cref="CacheEntryOptions.Tags"/> exceeds the supported tag count/length limits.</exception>
    ValueTask UpsertRawAsync(
        string key,
        ReadOnlySequence<byte> value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    );
}
