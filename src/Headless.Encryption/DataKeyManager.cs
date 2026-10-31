// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Checks;
using Microsoft.Extensions.Logging;

namespace Headless.Encryption;

internal sealed class DataKeyManager(
    DataKeyRing ring,
    IDataKeyStore store,
    IKeyEncryptionKeyProvider kek,
    DataKeyCache cache,
    ILogger<DataKeyManager> logger
) : IDataKeyManager
{
    private const int _RewrapPageSize = 100;

    public async Task EnsureAsync(string scope, CancellationToken cancellationToken = default)
    {
        DataKeyScope.Validate(scope, nameof(scope));

        // The explicit call is the one place a shredded scope is reopened. It asks the store rather than the cache, which
        // can still name a key that another host shredded within the cache duration.
        foreach (var kind in Enum.GetValues<DataKeyKind>())
        {
            cache.ForgetActive(scope, kind);

            using var _ = await ring.GetOrCreateActiveAsync(scope, kind, reopenShreddedScope: true, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<Guid> RotateAsync(string scope, DataKeyKind kind, CancellationToken cancellationToken = default)
    {
        DataKeyScope.Validate(scope, nameof(scope));
        Argument.IsTrue(Enum.IsDefined(kind), $"Unknown data key kind {kind}.", nameof(kind));

        var keys = await store.ListScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        var next = keys.Where(k => k.Kind == kind).Select(static k => k.Version).DefaultIfEmpty(0).Max() + 1;

        cache.ForgetActive(scope, kind);

        using (
            var created = await ring.TryCreateAsync(scope, kind, next, reopenShreddedScope: false, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            if (created is not null)
            {
                return created.KeyId;
            }
        }

        // A concurrent rotation took the version first, or a shred on this host removed the new key; either way the
        // store now says what is active.
        var active =
            await store.GetActiveAsync(scope, kind, cancellationToken).ConfigureAwait(false)
            ?? throw new DataKeyScopeShreddedException(scope);

        return active.KeyId;
    }

    public async Task<int> RewrapAsync(CancellationToken cancellationToken = default)
    {
        // Learned from the first wrap: keys already under this KEK version are skipped without a KMS call.
        string? currentKekId = null;
        Guid? after = null;
        var rewrapped = 0;

        while (true)
        {
            var page = await store.ListAsync(after, _RewrapPageSize, cancellationToken).ConfigureAwait(false);

            foreach (var stored in page)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_IsCurrent(stored, currentKekId))
                {
                    continue;
                }

                var key = await ring.UnwrapAsync(stored, cancellationToken).ConfigureAwait(false);

                try
                {
                    var wrapped = await kek.WrapAsync(key, DataKeyRing.Context(stored), cancellationToken)
                        .ConfigureAwait(false);
                    currentKekId = wrapped.KekId;

                    if (
                        !_IsCurrent(stored, currentKekId)
                        && await store
                            .ReplaceWrappedKeyAsync(stored.KeyId, kek.Name, wrapped, cancellationToken)
                            .ConfigureAwait(false)
                    )
                    {
                        rewrapped++;
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }

            if (page.Count < _RewrapPageSize)
            {
                break;
            }

            after = page[^1].KeyId;
        }

        logger.LogDataKeysRewrapped(rewrapped, currentKekId);

        return rewrapped;
    }

    public async Task<int> ShredAsync(string scope, CancellationToken cancellationToken = default)
    {
        DataKeyScope.Validate(scope, nameof(scope));

        var deleted = await store.ShredScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        cache.ForgetScope(scope);
        logger.LogDataKeysShredded(scope, deleted);

        return deleted;
    }

    private bool _IsCurrent(StoredDataKey stored, string? currentKekId) =>
        currentKekId is not null
        && string.Equals(stored.KekProvider, kek.Name, StringComparison.Ordinal)
        && string.Equals(stored.WrappedKey.KekId, currentKekId, StringComparison.Ordinal);
}
