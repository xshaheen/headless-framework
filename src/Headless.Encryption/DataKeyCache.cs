// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Headless.Encryption;

/// <summary>
/// The unwrapped data keys of this process, by key id, plus the active key id of each (scope, kind). Entries expire
/// after <see cref="EncryptionOptions.KeyCacheDuration" />, so a key shredded or rotated on another host stops being
/// used here within that time; the cache holds at most <see cref="EncryptionOptions.KeyCacheSize" /> keys.
/// </summary>
/// <remarks>
/// Keys live only in process memory, never in a distributed cache. An evicted key is cleared once the last operation
/// using it has released it, so eviction never clears a key under a running encryption.
/// </remarks>
internal sealed class DataKeyCache(TimeProvider timeProvider, EncryptionOptions options) : IDisposable
{
    private readonly ConcurrentDictionary<Guid, DataKeyEntry> _keys = new();
    private readonly ConcurrentDictionary<(string Scope, DataKeyKind Kind), ActiveKey> _active = new();

    // Each shred on this host takes the next generation and records it for its scope. A key read from the store before
    // a shred carries an older generation, so caching it afterwards is refused: an in-flight load cannot put a shredded
    // key back. The map holds only scopes shredded on this host.
    private readonly ConcurrentDictionary<string, long> _shreddedAt = new(StringComparer.Ordinal);
    private long _generation;

    public int Count => _keys.Count;

    /// <summary>Gets the shred generation; read it before the store read whose result will be cached.</summary>
    public long Generation => Volatile.Read(ref _generation);

    /// <summary>Takes a lease on a cached, unexpired key.</summary>
    public DataKeyLease? TryAcquire(Guid keyId)
    {
        if (!_keys.TryGetValue(keyId, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt <= timeProvider.GetUtcNow())
        {
            _Evict(keyId, entry);

            return null;
        }

        return entry.TryAcquire();
    }

    /// <summary>
    /// Caches <paramref name="key" />, which the cache now owns and clears on eviction, and returns a lease on it; or
    /// clears it and returns <see langword="null" /> when its scope was shredded on this host after
    /// <paramref name="generation" /> was read.
    /// </summary>
    public DataKeyLease? Add(StoredDataKey stored, byte[] key, long generation)
    {
        if (_ShreddedSince(stored.Scope, generation))
        {
            CryptographicOperations.ZeroMemory(key);

            return null;
        }

        var entry = new DataKeyEntry(stored, key, timeProvider.GetUtcNow() + options.KeyCacheDuration);

        // Taken before publishing, so the entry cannot be cleared by an eviction racing this call.
        var lease = entry.TryAcquire()!;

        if (_keys.Count >= options.KeyCacheSize)
        {
            _Trim();
        }

        // Not AddOrUpdate: its update factory can run more than once under contention, which would drop the
        // replaced entry's cache reference twice.
        while (true)
        {
            if (_keys.TryGetValue(stored.KeyId, out var previous))
            {
                if (_keys.TryUpdate(stored.KeyId, entry, previous))
                {
                    previous.Release();

                    break;
                }
            }
            else if (_keys.TryAdd(stored.KeyId, entry))
            {
                break;
            }
        }

        // A shred that began after the first check either recorded its generation before this read, or its eviction
        // pass runs after the entry was published and removes it.
        if (_ShreddedSince(stored.Scope, generation))
        {
            _Evict(stored.KeyId, entry);
            lease.Dispose();

            return null;
        }

        return lease;
    }

    public Guid? GetActive(string scope, DataKeyKind kind)
    {
        if (!_active.TryGetValue((scope, kind), out var active))
        {
            return null;
        }

        if (active.ExpiresAt <= timeProvider.GetUtcNow())
        {
            _active.TryRemove(KeyValuePair.Create((scope, kind), active));

            return null;
        }

        return active.KeyId;
    }

    public void SetActive(string scope, DataKeyKind kind, Guid keyId, long generation)
    {
        var active = new ActiveKey(keyId, timeProvider.GetUtcNow() + options.KeyCacheDuration);
        _active[(scope, kind)] = active;

        if (_ShreddedSince(scope, generation))
        {
            _active.TryRemove(KeyValuePair.Create((scope, kind), active));
        }
    }

    public void ForgetActive(string scope, DataKeyKind kind)
    {
        _active.TryRemove((scope, kind), out _);
    }

    /// <summary>Forgets every key and active id of <paramref name="scope" />.</summary>
    public void ForgetScope(string scope)
    {
        // Recorded before the eviction pass, so an entry published concurrently is either refused by Add or evicted here.
        _shreddedAt[scope] = Interlocked.Increment(ref _generation);

        foreach (var (keyId, entry) in _keys)
        {
            if (string.Equals(entry.Scope, scope, StringComparison.Ordinal))
            {
                _Evict(keyId, entry);
            }
        }

        foreach (var kind in Enum.GetValues<DataKeyKind>())
        {
            _active.TryRemove((scope, kind), out _);
        }
    }

    public void Dispose()
    {
        foreach (var (keyId, entry) in _keys)
        {
            _Evict(keyId, entry);
        }

        _active.Clear();
    }

    private bool _ShreddedSince(string scope, long generation) =>
        _shreddedAt.TryGetValue(scope, out var shreddedAt) && shreddedAt > generation;

    private void _Evict(Guid keyId, DataKeyEntry entry)
    {
        // Only the caller that removes the entry drops the cache's reference, so it is dropped exactly once.
        if (_keys.TryRemove(KeyValuePair.Create(keyId, entry)))
        {
            entry.Release();
        }
    }

    private void _Trim()
    {
        var now = timeProvider.GetUtcNow();
        var entries = _keys.ToArray();

        foreach (var (keyId, entry) in entries.Where(e => e.Value.ExpiresAt <= now))
        {
            _Evict(keyId, entry);
        }

        var excess = _keys.Count - options.KeyCacheSize + 1;

        if (excess <= 0)
        {
            return;
        }

        // The keys closest to expiry were cached first, so they go first.
        foreach (var (keyId, entry) in entries.OrderBy(e => e.Value.ExpiresAt).Take(excess))
        {
            _Evict(keyId, entry);
        }
    }

    private sealed record ActiveKey(Guid KeyId, DateTimeOffset ExpiresAt);
}

/// <summary>One cached, unwrapped data key, reference-counted so it is cleared only when nothing uses it.</summary>
internal sealed class DataKeyEntry(StoredDataKey stored, byte[] key, DateTimeOffset expiresAt)
{
    // One reference belongs to the cache; each lease adds one.
    private int _references = 1;

    public Guid KeyId { get; } = stored.KeyId;

    public string Scope { get; } = stored.Scope;

    public DataKeyKind Kind { get; } = stored.Kind;

    public DateTimeOffset ExpiresAt { get; } = expiresAt;

    public ReadOnlySpan<byte> Key => key;

    public DataKeyLease? TryAcquire()
    {
        while (true)
        {
            var current = Volatile.Read(ref _references);

            if (current == 0)
            {
                return null;
            }

            if (Interlocked.CompareExchange(ref _references, current + 1, current) == current)
            {
                return new DataKeyLease(this);
            }
        }
    }

    public void Release()
    {
        if (Interlocked.Decrement(ref _references) == 0)
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}

/// <summary>A use of one cached data key; disposing it releases the key.</summary>
internal sealed class DataKeyLease(DataKeyEntry entry) : IDisposable
{
    private int _disposed;

    public Guid KeyId => entry.KeyId;

    public string Scope => entry.Scope;

    public DataKeyKind Kind => entry.Kind;

    public ReadOnlySpan<byte> Key => entry.Key;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            entry.Release();
        }
    }
}
