// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>A data-key store kept in a list behind a lock, shared by every test host built on it.</summary>
public sealed class InMemoryDataKeyStore : IDataKeyStore
{
    private readonly Lock _gate = new();
    private readonly List<StoredDataKey> _keys = [];
    private readonly HashSet<string> _shredded = new(StringComparer.Ordinal);

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

    public Task<DataKeyInsertResult> TryInsertAsync(
        StoredDataKey key,
        bool reopenShreddedScope,
        CancellationToken cancellationToken
    )
    {
        lock (_gate)
        {
            InsertAttempts++;

            if (_shredded.Contains(key.Scope))
            {
                if (!reopenShreddedScope)
                {
                    return Task.FromResult(DataKeyInsertResult.ScopeShredded);
                }

                _shredded.Remove(key.Scope);
            }

            if (
                _keys.Exists(k =>
                    string.Equals(k.Scope, key.Scope, StringComparison.Ordinal)
                    && k.Kind == key.Kind
                    && k.Version == key.Version
                )
            )
            {
                return Task.FromResult(DataKeyInsertResult.VersionTaken);
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

            return Task.FromResult(DataKeyInsertResult.Inserted);
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

    public Task<int> ShredScopeAsync(string scope, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _shredded.Add(scope);

            return Task.FromResult(_keys.RemoveAll(k => string.Equals(k.Scope, scope, StringComparison.Ordinal)));
        }
    }

    public Task<bool> DeleteSupersededVersionAsync(
        string scope,
        DataKeyKind kind,
        int version,
        CancellationToken cancellationToken
    )
    {
        lock (_gate)
        {
            var sameKind = _keys.FindAll(k =>
                string.Equals(k.Scope, scope, StringComparison.Ordinal) && k.Kind == kind
            );

            if (!sameKind.Exists(k => k.Version > version))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(
                _keys.RemoveAll(k =>
                    string.Equals(k.Scope, scope, StringComparison.Ordinal) && k.Kind == kind && k.Version == version
                ) > 0
            );
        }
    }

    /// <summary>Registers this instance as the key store of an encryption setup.</summary>
    public IEncryptionProviderOptionsExtension AsExtension() => new Extension(this);

    private sealed class Extension(InMemoryDataKeyStore store) : IEncryptionProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services) => services.AddSingleton<IDataKeyStore>(store);
    }
}
