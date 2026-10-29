// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using Headless.Checks;
using Microsoft.Extensions.Logging;

namespace Headless.Encryption;

/// <summary>
/// Finds data keys for the encryptor and the manager: from the cache, else from the store, unwrapped by the
/// key-encryption key. Creates a scope's first key on demand, race-safe across hosts.
/// </summary>
#pragma warning disable CA2000 // False positive: every lease taken here is returned, so the caller owns and disposes it.
internal sealed class DataKeyRing(
    IDataKeyStore store,
    IKeyEncryptionKeyProvider kek,
    DataKeyCache cache,
    ILogger<DataKeyRing> logger
)
{
    // A key cached between the store read and the lease can only be gone again if it was evicted in that window;
    // a few rounds always suffice.
    private const int _MaxAcquireRounds = 3;

    // Concurrent misses for one key share one unwrap, so a burst of requests for a cold tenant costs one KMS call.
    private readonly ConcurrentDictionary<Guid, Lazy<Task<StoredDataKey>>> _loading = new();

    /// <summary>Leases the active key of a scope and kind, creating version 1 when the scope has none.</summary>
    public async ValueTask<DataKeyLease> GetOrCreateActiveAsync(
        string scope,
        DataKeyKind kind,
        CancellationToken cancellationToken
    )
    {
        for (var round = 0; round < _MaxAcquireRounds; round++)
        {
            if (cache.GetActive(scope, kind) is { } cachedId && cache.TryAcquire(cachedId) is { } cached)
            {
                return cached;
            }

            var stored = await store.GetActiveAsync(scope, kind, cancellationToken).ConfigureAwait(false);

            if (stored is null)
            {
                var created = await TryCreateAsync(scope, kind, version: 1, cancellationToken).ConfigureAwait(false);

                if (created is not null)
                {
                    cache.SetActive(scope, kind, created.KeyId);

                    return created;
                }

                // Another host inserted version 1 first; read the key it stored.
                continue;
            }

            if (await _LoadAsync(stored, cancellationToken).ConfigureAwait(false) is { } loaded)
            {
                cache.SetActive(scope, kind, stored.KeyId);

                return loaded;
            }
        }

        throw new InvalidOperationException(
            $"The active {kind} data key of scope '{scope}' could not be read after {_MaxAcquireRounds} attempts; it "
                + "was deleted while it was being read."
        );
    }

    /// <summary>Leases the active key of a scope and kind, or returns <see langword="null" /> when it has none.</summary>
    public async ValueTask<DataKeyLease?> TryGetActiveAsync(
        string scope,
        DataKeyKind kind,
        CancellationToken cancellationToken
    )
    {
        if (cache.GetActive(scope, kind) is { } cachedId && cache.TryAcquire(cachedId) is { } cached)
        {
            return cached;
        }

        var stored = await store.GetActiveAsync(scope, kind, cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        var lease = await _LoadAsync(stored, cancellationToken).ConfigureAwait(false);

        if (lease is not null)
        {
            cache.SetActive(scope, kind, stored.KeyId);
        }

        return lease;
    }

    /// <summary>Returns the id of the active key of a scope and kind, or <see langword="null" /> when it has none.</summary>
    public async ValueTask<Guid?> GetActiveIdAsync(string scope, DataKeyKind kind, CancellationToken cancellationToken)
    {
        if (cache.GetActive(scope, kind) is { } cachedId)
        {
            return cachedId;
        }

        var stored = await store.GetActiveAsync(scope, kind, cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        cache.SetActive(scope, kind, stored.KeyId);

        return stored.KeyId;
    }

    /// <summary>Leases a key by id, or returns <see langword="null" /> when no key has the id.</summary>
    public async ValueTask<DataKeyLease?> TryGetAsync(Guid keyId, CancellationToken cancellationToken)
    {
        for (var round = 0; round < _MaxAcquireRounds; round++)
        {
            if (cache.TryAcquire(keyId) is { } cached)
            {
                return cached;
            }

            var stored = await store.GetAsync(keyId, cancellationToken).ConfigureAwait(false);

            if (stored is null)
            {
                return null;
            }

            if (await _LoadAsync(stored, cancellationToken).ConfigureAwait(false) is { } loaded)
            {
                return loaded;
            }
        }

        return null;
    }

    /// <summary>Unwraps a stored key with the configured key-encryption key. The caller owns and clears the result.</summary>
    public async ValueTask<byte[]> UnwrapAsync(StoredDataKey stored, CancellationToken cancellationToken)
    {
        if (!string.Equals(stored.KekProvider, kek.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Data key {stored.KeyId} was wrapped by the '{stored.KekProvider}' key-encryption-key provider, but "
                    + $"the configured provider is '{kek.Name}'."
            );
        }

        var key = await kek.UnwrapAsync(stored.WrappedKey, Context(stored), cancellationToken).ConfigureAwait(false);

        if (key.Length != FieldCipher.KeyLength)
        {
            CryptographicOperations.ZeroMemory(key);

            throw new CryptographicException(
                $"The '{kek.Name}' key-encryption-key provider unwrapped data key {stored.KeyId} to {key.Length} "
                    + $"bytes; a data key is {FieldCipher.KeyLength} bytes."
            );
        }

        return key;
    }

    /// <summary>
    /// Generates, wraps, and inserts the key <paramref name="version" /> of a scope and kind, caching it. Returns a lease
    /// on it, or <see langword="null" /> when another writer took that version first.
    /// </summary>
    public async ValueTask<DataKeyLease?> TryCreateAsync(
        string scope,
        DataKeyKind kind,
        int version,
        CancellationToken cancellationToken
    )
    {
        var key = RandomNumberGenerator.GetBytes(FieldCipher.KeyLength);

        try
        {
            var keyId = Guid.CreateVersion7();
            var context = new KeyWrapContext
            {
                Scope = scope,
                KeyId = keyId,
                Kind = kind,
            };
            var wrapped = await kek.WrapAsync(key, context, cancellationToken).ConfigureAwait(false);
            var stored = new StoredDataKey
            {
                KeyId = keyId,
                Scope = scope,
                Kind = kind,
                Version = version,
                KekProvider = Argument.HasMaxLength(kek.Name, DataKeyFieldLimits.KekProviderMaxLength),
                WrappedKey = wrapped,
            };

            if (!await store.TryInsertAsync(stored, cancellationToken).ConfigureAwait(false))
            {
                CryptographicOperations.ZeroMemory(key);

                return null;
            }

            logger.LogDataKeyCreated(kind, keyId, version, scope);

            // The cache now owns the array and clears it on eviction.
            return cache.Add(stored, key);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);

            throw;
        }
    }

    private async ValueTask<DataKeyLease?> _LoadAsync(StoredDataKey stored, CancellationToken cancellationToken)
    {
        // The shared unwrap runs uncancelled: one caller giving up must not fail the others waiting on it, so a
        // canceled caller only stops waiting.
        var loading = _loading.GetOrAdd(
            stored.KeyId,
            static (_, state) => new Lazy<Task<StoredDataKey>>(() => state.Ring._UnwrapIntoCacheAsync(state.Stored)),
            (Ring: this, Stored: stored)
        );

        try
        {
            var loaded = await loading.Value.WaitAsync(cancellationToken).ConfigureAwait(false);

            return cache.TryAcquire(loaded.KeyId);
        }
        finally
        {
            _loading.TryRemove(KeyValuePair.Create(stored.KeyId, loading));
        }
    }

    private async Task<StoredDataKey> _UnwrapIntoCacheAsync(StoredDataKey stored)
    {
        var key = await UnwrapAsync(stored, CancellationToken.None).ConfigureAwait(false);

        // The cache owns the array from here; the lease is only needed to publish the entry.
        using var lease = cache.Add(stored, key);

        return stored;
    }

    public static KeyWrapContext Context(StoredDataKey stored) =>
        new()
        {
            Scope = stored.Scope,
            KeyId = stored.KeyId,
            Kind = stored.Kind,
        };
}
#pragma warning restore CA2000
