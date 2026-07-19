// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Tests;

internal sealed class FakeFactoryCacheStore : IFactoryCacheStore
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public int TryGetEntryCalls { get; private set; }

    public int TryGetAllEntriesCalls { get; private set; }

    public int SetEntryCalls { get; private set; }

    public int RearmCalls { get; private set; }

    public Func<Exception>? TryGetEntryFault { get; set; }

    public Func<Exception>? SetEntryFault { get; set; }

    public Func<Exception>? RearmFault { get; set; }

    public Func<string, int, Entry?>? TryGetEntryOverride { get; set; }

    /// <summary>
    /// Forces a <see cref="SetEntryAsync{T}"/> call to report CAS-lost (return <see langword="false"/>) without mutating the
    /// stored entry, modelling a concurrent writer winning the compare-and-swap. Invoked with the key and the
    /// post-increment <see cref="SetEntryCalls"/> count; return <see langword="false"/> to fail that write, <see langword="true"/>
    /// (or leave unset) to commit normally.
    /// </summary>
    public Func<string, int, bool>? SetEntryCommitOverride { get; set; }

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
        DateTime? createdAt = null,
        IReadOnlyCollection<string>? tags = null,
        bool serveStaleImmediately = false
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
                CreatedAt: createdAt,
                Tags: tags,
                ConcurrencyStamp: Guid.NewGuid().ToString("N"),
                ServeStaleImmediately: serveStaleImmediately
            );
        }
    }

    public void RemoveEntry(string key)
    {
        lock (_lock)
        {
            _entries.Remove(key);
        }
    }

    public ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(
        string key,
        FactoryCacheReadOptions readOptions = default,
        CancellationToken cancellationToken = default
    )
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
                    CreatedAt = entry.CreatedAt,
                    Tags = entry.Tags,
                    ConcurrencyStamp = entry.ConcurrencyStamp,
                    ServeStaleImmediately = entry.ServeStaleImmediately,
                }
            );
        }
    }

    public async ValueTask<CacheStoreEntry<T>[]> TryGetAllEntriesAsync<T>(
        IReadOnlyList<string> keys,
        FactoryCacheReadOptions readOptions = default,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryGetAllEntriesCalls++;

        // Position-aligned per-key resolution over the single-key primitive; counts one bulk call regardless of the
        // number of keys so O(1)-not-O(N) fan-out is observable via TryGetAllEntriesCalls vs TryGetEntryCalls.
        var result = new CacheStoreEntry<T>[keys.Count];

        for (var i = 0; i < keys.Count; i++)
        {
            result[i] = await TryGetEntryAsync<T>(keys[i], cancellationToken: cancellationToken);
        }

        return result;
    }

    public ValueTask<bool> SetEntryAsync<T>(
        string key,
        in CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetEntryCalls++;

        if (SetEntryFault is not null)
        {
            throw SetEntryFault();
        }

        if (SetEntryCommitOverride is not null && !SetEntryCommitOverride(key, SetEntryCalls))
        {
            return new ValueTask<bool>(false);
        }

        lock (_lock)
        {
            if (
                entry.ExpectedConcurrencyStamp is not null
                && (
                    !_entries.TryGetValue(key, out var currentEntry)
                    || !string.Equals(
                        currentEntry.ConcurrencyStamp,
                        entry.ExpectedConcurrencyStamp,
                        StringComparison.Ordinal
                    )
                )
            )
            {
                return new ValueTask<bool>(false);
            }

            _entries[key] = new Entry(
                Value: entry.Value,
                IsNull: entry.IsNull,
                LogicalExpiresAt: entry.LogicalExpiresAt,
                PhysicalExpiresAt: entry.PhysicalExpiresAt,
                SlidingExpiration: entry.SlidingExpiration,
                EagerRefreshAt: entry.EagerRefreshAt,
                ETag: entry.ETag,
                LastModifiedAt: entry.LastModifiedAt,
                CreatedAt: entry.CreatedAt,
                Tags: entry.Tags,
                ConcurrencyStamp: Guid.NewGuid().ToString("N")
            );
        }

        return new ValueTask<bool>(true);
    }

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
        DateTime? CreatedAt = null,
        IReadOnlyCollection<string>? Tags = null,
        string ConcurrencyStamp = "",
        bool ServeStaleImmediately = false
    );
}
