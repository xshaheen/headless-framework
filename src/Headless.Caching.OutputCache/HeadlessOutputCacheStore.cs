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
    : IOutputCacheStore,
        IOutputCacheBufferStore
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

        var value = await cache.GetAsync<byte[]>(key, cancellationToken).ConfigureAwait(false);

        if (!value.HasValue)
        {
            return false;
        }

        await destination.WriteAsync(value.Value, cancellationToken).ConfigureAwait(false);

        return true;
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

        // The pooled buffer and tags span are valid only for the duration of this call (the buffer-store
        // contract), so materialize both before the first await that may yield — reading recycled memory after
        // the engine resumes would corrupt the entry.
        var bytes = value.ToArray();
        var tagsCopy = tags.IsEmpty ? null : tags.ToArray();

        return _UpsertAsync(key, bytes, tagsCopy, validFor, cancellationToken);
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
            // The byte[] overload passes tags straight through (it may hand us an empty array); the buffer overload
            // pre-normalizes empty to null before its copy. Coerce here so both paths index nothing for a tagless
            // entry rather than registering an empty tag set with the engine.
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
