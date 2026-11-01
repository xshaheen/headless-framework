// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Encryption;

/// <summary>
/// Stores wrapped data keys, one row per (scope, kind, version). The store never sees an unwrapped key.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by key-store provider packages (<c>Headless.Encryption.PostgreSql</c>,
/// <c>Headless.Encryption.SqlServer</c>) and open to third parties: everything a store needs is in this interface.
/// Register one through the provider's <c>Use…</c> member on the encryption setup builder; exactly one store is
/// allowed. Every member is safe to call concurrently from several hosts.
/// </para>
/// <para>New members arrive as default interface members, so an existing store keeps compiling.</para>
/// </remarks>
[PublicAPI]
public interface IDataKeyStore
{
    /// <summary>Reads one key by id.</summary>
    /// <param name="keyId">The key's id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The key, or <see langword="null" /> when no key has the id.</returns>
    Task<StoredDataKey?> GetAsync(Guid keyId, CancellationToken cancellationToken);

    /// <summary>Reads the active key of a scope and kind: the one with the highest version.</summary>
    /// <param name="scope">The scope, compared ordinally.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The key, or <see langword="null" /> when the scope has no key of that kind.</returns>
    Task<StoredDataKey?> GetActiveAsync(string scope, DataKeyKind kind, CancellationToken cancellationToken);

    /// <summary>Reads every key of a scope, every kind and version, in no particular order.</summary>
    /// <param name="scope">The scope, compared ordinally.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The keys; empty when the scope has none.</returns>
    Task<IReadOnlyList<StoredDataKey>> ListScopeAsync(string scope, CancellationToken cancellationToken);

    /// <summary>
    /// Reads up to <paramref name="limit" /> keys of every scope, ordered by id, after <paramref name="afterKeyId" />.
    /// </summary>
    /// <param name="afterKeyId">The last id of the previous page, or <see langword="null" /> for the first page.</param>
    /// <param name="limit">The page size, at least 1.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page; shorter than <paramref name="limit" /> only at the end.</returns>
    Task<IReadOnlyList<StoredDataKey>> ListAsync(Guid? afterKeyId, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts <paramref name="key" /> unless its scope already has a key of the same kind and version, or the scope was
    /// shredded, without raising a duplicate-key error. Two hosts racing on one version agree on the one that was
    /// inserted, and an insert racing <see cref="ShredScopeAsync" /> either lands before the shred (which then deletes
    /// it) or sees the shred and is refused.
    /// </summary>
    /// <param name="key">The key to insert; <see cref="StoredDataKey.CreatedAt" /> is set by the store.</param>
    /// <param name="reopenShreddedScope">
    /// <see langword="true" /> to clear the scope's shredded marker and insert anyway, the explicit reopen
    /// <see cref="IDataKeyManager.EnsureAsync" /> performs; <see langword="false" /> to refuse a shredded scope.
    /// </param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether the key was inserted, its version was taken, or its scope is shredded.</returns>
    Task<DataKeyInsertResult> TryInsertAsync(
        StoredDataKey key,
        bool reopenShreddedScope,
        CancellationToken cancellationToken
    );

    /// <summary>Replaces the wrapped form of one key, after its key-encryption key was rotated.</summary>
    /// <param name="keyId">The key's id.</param>
    /// <param name="kekProvider">The provider that wrapped <paramref name="wrappedKey" />.</param>
    /// <param name="wrappedKey">The key wrapped again.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the key existed and was updated.</returns>
    Task<bool> ReplaceWrappedKeyAsync(
        Guid keyId,
        string kekProvider,
        WrappedDataKey wrappedKey,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Deletes every key of a scope and marks the scope shredded, in one transaction, so no key can be created for it
    /// again until an insert reopens it.
    /// </summary>
    /// <param name="scope">The scope, compared ordinally.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The number of keys deleted.</returns>
    Task<int> ShredScopeAsync(string scope, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes version <paramref name="version" /> of a scope's key of <paramref name="kind" />, only while a newer
    /// version of that scope and kind exists, so the active version is never deleted.
    /// </summary>
    /// <param name="scope">The scope, compared ordinally.</param>
    /// <param name="kind">The kind of key.</param>
    /// <param name="version">The version to delete.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the version was deleted.</returns>
    Task<bool> DeleteSupersededVersionAsync(
        string scope,
        DataKeyKind kind,
        int version,
        CancellationToken cancellationToken
    );
}
