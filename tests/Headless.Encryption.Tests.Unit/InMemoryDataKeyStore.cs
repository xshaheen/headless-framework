// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>A data-key store kept in a list behind a lock, shared by every test host built on it.</summary>
public sealed class InMemoryDataKeyStore : IDataKeyStore
{
    private readonly Lock _gate = new();
    private readonly List<StoredDataKey> _keys = [];

    public int InsertAttempts { get; private set; }

    public IReadOnlyList<StoredDataKey> Snapshot()
    {
        lock (_gate)
        {
            return [.. _keys];
        }
    }

    public Task<StoredDataKey?> GetAsync(Guid keyId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_keys.Find(k => k.KeyId == keyId));
        }
    }

    public Task<StoredDataKey?> GetActiveAsync(string scope, DataKeyKind kind, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(
                _keys
                    .Where(k => string.Equals(k.Scope, scope, StringComparison.Ordinal) && k.Kind == kind)
                    .MaxBy(k => k.Version)
            );
        }
    }

    public Task<IReadOnlyList<StoredDataKey>> ListScopeAsync(string scope, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<StoredDataKey>>([
                .. _keys.Where(k => string.Equals(k.Scope, scope, StringComparison.Ordinal)),
            ]);
        }
    }

    public Task<IReadOnlyList<StoredDataKey>> ListAsync(
        Guid? afterKeyId,
        int limit,
        CancellationToken cancellationToken
    )
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<StoredDataKey>>([
                .. _keys
                    .Where(k => afterKeyId is null || k.KeyId.CompareTo(afterKeyId.Value) > 0)
                    .OrderBy(k => k.KeyId)
                    .Take(limit),
            ]);
        }
    }

    public Task<bool> TryInsertAsync(StoredDataKey key, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            InsertAttempts++;

            if (
                _keys.Exists(k =>
                    string.Equals(k.Scope, key.Scope, StringComparison.Ordinal)
                    && k.Kind == key.Kind
                    && k.Version == key.Version
                )
            )
            {
                return Task.FromResult(false);
            }

            _keys.Add(
                new StoredDataKey
                {
                    KeyId = key.KeyId,
                    Scope = key.Scope,
                    Kind = key.Kind,
                    Version = key.Version,
                    KekProvider = key.KekProvider,
                    WrappedKey = key.WrappedKey,
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            );

            return Task.FromResult(true);
        }
    }

    public Task<bool> ReplaceWrappedKeyAsync(
        Guid keyId,
        string kekProvider,
        WrappedDataKey wrappedKey,
        CancellationToken cancellationToken
    )
    {
        lock (_gate)
        {
            var index = _keys.FindIndex(k => k.KeyId == keyId);

            if (index < 0)
            {
                return Task.FromResult(false);
            }

            var key = _keys[index];
            _keys[index] = new StoredDataKey
            {
                KeyId = key.KeyId,
                Scope = key.Scope,
                Kind = key.Kind,
                Version = key.Version,
                KekProvider = kekProvider,
                WrappedKey = wrappedKey,
                CreatedAt = key.CreatedAt,
            };

            return Task.FromResult(true);
        }
    }

    public Task<int> DeleteScopeAsync(string scope, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_keys.RemoveAll(k => string.Equals(k.Scope, scope, StringComparison.Ordinal)));
        }
    }

    /// <summary>Registers this instance as the key store of an encryption setup.</summary>
    public IEncryptionProviderOptionsExtension AsExtension() => new Extension(this);

    private sealed class Extension(InMemoryDataKeyStore store) : IEncryptionProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services) => services.AddSingleton<IDataKeyStore>(store);
    }
}
