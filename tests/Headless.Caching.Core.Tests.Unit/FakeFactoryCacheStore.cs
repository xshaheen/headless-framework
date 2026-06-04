// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Tests;

internal sealed class FakeFactoryCacheStore : IFactoryCacheStore
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public int TryGetEntryCalls { get; private set; }

    public int SetEntryCalls { get; private set; }

    public Func<Exception>? TryGetEntryFault { get; set; }

    public Func<Exception>? SetEntryFault { get; set; }

    public Entry? GetEntry(string key)
    {
        lock (_lock)
        {
            return _entries.GetValueOrDefault(key);
        }
    }

    public void SetEntry<T>(string key, T? value, DateTime logicalExpiresAt, DateTime physicalExpiresAt)
    {
        lock (_lock)
        {
            _entries[key] = new Entry(
                Value: value,
                IsNull: value is null,
                LogicalExpiresAt: logicalExpiresAt,
                PhysicalExpiresAt: physicalExpiresAt
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

        lock (_lock)
        {
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
                    PhysicalExpiresAt: entry.PhysicalExpiresAt
                )
            );
        }
    }

    public ValueTask SetEntryAsync<T>(
        string key,
        T? value,
        bool isNull,
        DateTime logicalExpiresAt,
        DateTime physicalExpiresAt,
        CancellationToken cancellationToken
    )
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
                Value: value,
                IsNull: isNull,
                LogicalExpiresAt: logicalExpiresAt,
                PhysicalExpiresAt: physicalExpiresAt
            );
        }

        return ValueTask.CompletedTask;
    }

    internal sealed record Entry(object? Value, bool IsNull, DateTime LogicalExpiresAt, DateTime PhysicalExpiresAt);
}
