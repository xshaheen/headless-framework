// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;

namespace Headless.Caching;

/// <summary>
/// Defines capabilities for caches that read and write raw payloads without allocating intermediate byte arrays.
/// </summary>
[PublicAPI]
public interface IBufferCache
{
    /// <summary>
    /// Reads the payload for <paramref name="key"/> into <paramref name="destination"/> without
    /// allocating an intermediate byte array, honoring logical expiry and tag invalidation semantics.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="destination">The buffer writer into which the payload is written.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> when the payload is found and written to <paramref name="destination"/>; otherwise, <see langword="false"/>.</returns>
    ValueTask<bool> TryGetToAsync(
        string key,
        IBufferWriter<byte> destination,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Upserts the payload from <paramref name="value"/> without allocating an intermediate byte array,
    /// applying <see cref="CacheEntryOptions"/> metadata. The sequence is consumed synchronously before
    /// any asynchronous wait, allowing callers to provide pooled buffers valid only for the call duration.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The raw payload to persist.</param>
    /// <param name="options">The cache entry options applied to the written entry.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentException"><see cref="CacheEntryOptions.Tags"/> exceeds supported tag count or length limits.</exception>
    ValueTask UpsertRawAsync(
        string key,
        ReadOnlySequence<byte> value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    );
}
