// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using Headless.Checks;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;

namespace Headless.Caching;

/// <summary>
/// Adapts a named Headless <see cref="ICache"/> instance to ASP.NET Core's <see cref="IOutputCacheStore"/> and
/// <see cref="IOutputCacheBufferStore"/>. The stored <c>value</c> is an opaque blob (a serialized
/// <c>OutputCacheEntry</c>) the store persists and returns verbatim; all caching behavior — distribution, L1+L2,
/// and cluster-wide tag invalidation — comes from the engine the consumer composed.
/// </summary>
internal sealed class HeadlessOutputCacheStore(ICache cache, IOptions<HeadlessOutputCacheStoreOptions> options)
    : IOutputCacheBufferStore
{
    private readonly HeadlessOutputCacheStoreOptions _options = Argument.IsNotNull(options).Value;

    /// <inheritdoc />
    public async ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        Argument.IsNotNullOrEmpty(key);

        var value = await cache.GetAsync<byte[]>(key, cancellationToken).ConfigureAwait(false);

        return value.HasValue ? value.Value : null;
    }

    /// <inheritdoc />
    public ValueTask SetAsync(
        string key,
        byte[] value,
        string[]? tags,
        TimeSpan validFor,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(value);

        return _UpsertAsync(key, value, tags, validFor, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
    {
        Argument.IsNotNullOrEmpty(tag);

        return cache.RemoveByTagAsync(tag, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryGetAsync(string key, PipeWriter destination, CancellationToken cancellationToken)
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(destination);

        // PipeWriter is an IBufferWriter<byte>: when the named cache implements IBufferCache (Redis/InMemory/Hybrid)
        // the payload is written straight into the pipe with no intermediate byte[]; otherwise the helper falls back
        // to GetAsync<byte[]> + a single copy. The helper writes via IBufferWriter.Write (Advance, no implicit
        // flush), so flush the pipe on a hit to match the previous WriteAsync semantics the formatter relies on.
        var hit = await cache.TryGetToOrFallbackAsync(key, destination, cancellationToken).ConfigureAwait(false);

        if (hit)
        {
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        return hit;
    }

    /// <inheritdoc />
    public ValueTask SetAsync(
        string key,
        ReadOnlySequence<byte> value,
        ReadOnlyMemory<string> tags,
        TimeSpan validFor,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNullOrEmpty(key);

        // The pooled tags span is valid only for this call, so materialize it before delegating. The value
        // sequence is consumed synchronously by UpsertRawOrFallbackAsync (the IBufferCache raw fast path frames it
        // without an intermediate byte[]; the fallback copies it once) — invoked here in the synchronous body,
        // before this method returns and ASP.NET recycles the buffers.
        var tagsCopy = tags.IsEmpty ? null : tags.ToArray();
        var entryOptions = new CacheEntryOptions { Duration = _ResolveDuration(validFor), Tags = tagsCopy };

        return cache.UpsertRawOrFallbackAsync(key, value, entryOptions, cancellationToken);
    }

    private async ValueTask _UpsertAsync(
        string key,
        byte[] value,
        string[]? tags,
        TimeSpan validFor,
        CancellationToken cancellationToken
    )
    {
        var entryOptions = new CacheEntryOptions
        {
            Duration = _ResolveDuration(validFor),
            // The byte[] SetAsync overload passes tags straight through (it may hand us an empty array); coerce an
            // empty set to null so a tagless entry indexes nothing rather than registering an empty tag set.
            Tags = tags is { Length: > 0 } ? tags : null,
        };

        // Discard the upsert-occurred bool: the output-cache contract has no notion of insert-vs-update.
        await cache.UpsertEntryAsync(key, value, entryOptions, cancellationToken).ConfigureAwait(false);
    }

    // A positive validFor is the relative TTL ASP.NET hands us; a non-positive validFor (an edge the middleware
    // can produce) falls back to the configured default rather than expiring the entry immediately.
    private TimeSpan _ResolveDuration(TimeSpan validFor) =>
        validFor > TimeSpan.Zero ? validFor : _options.DefaultExpiration;
}
