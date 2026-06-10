// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Tests;

internal sealed class FakeFactoryCacheStore : IFactoryCacheStore
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public int TryGetEntryCalls { get; private set; }

    public int SetEntryCalls { get; private set; }

    public int RearmCalls { get; private set; }

    public Func<Exception>? TryGetEntryFault { get; set; }

    public Func<Exception>? SetEntryFault { get; set; }

    public Func<Exception>? RearmFault { get; set; }

    public Func<string, int, Entry?>? TryGetEntryOverride { get; set; }

    public Entry? GetEntry(string key)
    {
        lock (_lock)
        {
            return _entries.GetValueOrDefault(key);
        }
    }

    public void SetEntry<T>(
        string key,
        T? value,
        DateTime logicalExpiresAt,
        DateTime physicalExpiresAt,
        TimeSpan? slidingExpiration = null,
        DateTime? eagerRefreshAt = null,
        string? etag = null,
        DateTime? lastModifiedAt = null,
        IReadOnlyCollection<string>? tags = null
    )
    {
        lock (_lock)
        {
            _entries[key] = new Entry(
                Value: value,
                IsNull: value is null,
                LogicalExpiresAt: logicalExpiresAt,
                PhysicalExpiresAt: physicalExpiresAt,
                SlidingExpiration: slidingExpiration,
                EagerRefreshAt: eagerRefreshAt,
                ETag: etag,
                LastModifiedAt: lastModifiedAt,
                Tags: tags
            );
        }
    }

    public ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryGetEntryCalls++;

        if (TryGetEntryFault is not null)
        {
            throw TryGetEntryFault();
        }

        var overrideEntry = TryGetEntryOverride?.Invoke(key, TryGetEntryCalls);

        lock (_lock)
        {
            if (overrideEntry is not null)
            {
                _entries[key] = overrideEntry;
            }

            if (!_entries.TryGetValue(key, out var entry))
            {
                return new ValueTask<CacheStoreEntry<T>>(CacheStoreEntry<T>.NotFound);
            }

            return new ValueTask<CacheStoreEntry<T>>(
                new CacheStoreEntry<T>(
                    Found: true,
                    IsNull: entry.IsNull,
                    Value: (T?)entry.Value,
                    LogicalExpiresAt: entry.LogicalExpiresAt,
                    PhysicalExpiresAt: entry.PhysicalExpiresAt,
                    SlidingExpiration: entry.SlidingExpiration
                )
                {
                    EagerRefreshAt = entry.EagerRefreshAt,
                    ETag = entry.ETag,
                    LastModifiedAt = entry.LastModifiedAt,
                    Tags = entry.Tags,
                }
            );
        }
    }

    public ValueTask SetEntryAsync<T>(string key, in CacheStoreEntryWrite<T> entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetEntryCalls++;

        if (SetEntryFault is not null)
        {
            throw SetEntryFault();
        }

        lock (_lock)
        {
            _entries[key] = new Entry(
                Value: entry.Value,
                IsNull: entry.IsNull,
                LogicalExpiresAt: entry.LogicalExpiresAt,
                PhysicalExpiresAt: entry.PhysicalExpiresAt,
                SlidingExpiration: entry.SlidingExpiration,
                EagerRefreshAt: entry.EagerRefreshAt,
                ETag: entry.ETag,
                LastModifiedAt: entry.LastModifiedAt,
                Tags: entry.Tags
            );

            LastRemovedTags = entry.RemovedTags;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>The <see cref="CacheStoreEntryWrite{T}.RemovedTags"/> carried by the most recent write.</summary>
    public IReadOnlyCollection<string>? LastRemovedTags { get; private set; }

    public ValueTask TryRearmSlidingAsync(
        string key,
        TimeSpan slidingExpiration,
        DateTime physicalExpiresAt,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        RearmCalls++;

        if (RearmFault is not null)
        {
            throw RearmFault();
        }

        lock (_lock)
        {
            // Models a metadata-only re-arm: extend the stored entry's logical deadline in place, keeping the
            // value, physical cap, and sliding window. Mirrors the throttle the real stores apply (re-arm only
            // once at least half the idle window has elapsed) so coordinator throttle behavior is observable.
            if (
                !_entries.TryGetValue(key, out var entry)
                || entry.SlidingExpiration is null
                || physicalExpiresAt <= now
            )
            {
                return ValueTask.CompletedTask;
            }

            var remaining = entry.LogicalExpiresAt - now;

            if (remaining > TimeSpan.FromTicks(slidingExpiration.Ticks / 2))
            {
                return ValueTask.CompletedTask;
            }

            var rearmed = now.Add(slidingExpiration);

            if (rearmed > physicalExpiresAt)
            {
                rearmed = physicalExpiresAt;
            }

            if (rearmed <= entry.LogicalExpiresAt)
            {
                return ValueTask.CompletedTask;
            }

            _entries[key] = entry with { LogicalExpiresAt = rearmed };
        }

        return ValueTask.CompletedTask;
    }

    internal sealed record Entry(
        object? Value,
        bool IsNull,
        DateTime LogicalExpiresAt,
        DateTime PhysicalExpiresAt,
        TimeSpan? SlidingExpiration,
        DateTime? EagerRefreshAt = null,
        string? ETag = null,
        DateTime? LastModifiedAt = null,
        IReadOnlyCollection<string>? Tags = null
    );
}
