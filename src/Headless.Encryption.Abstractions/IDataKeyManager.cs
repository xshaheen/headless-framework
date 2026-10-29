// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Encryption;

/// <summary>
/// Manages the data keys behind <see cref="IFieldEncryptor" />: creates them for a scope, rotates them, re-wraps them
/// under a new key-encryption key, and shreds a scope by deleting its keys.
/// </summary>
/// <remarks>
/// A scope is a tenant id, <see cref="FieldContext.HostScope" /> for host-level data, or a per-subject scope. Each
/// scope holds a versioned key per <see cref="DataKeyKind" />; the highest version is active and encrypts new values,
/// and older versions keep decrypting the values they encrypted. Implemented by Headless; applications call it.
/// </remarks>
[PublicAPI]
public interface IDataKeyManager
{
    /// <summary>
    /// Creates version 1 of each key kind the scope lacks, so a tenant's onboarding fails early when the key-encryption
    /// key is unreachable. Safe to call concurrently from several hosts: they agree on one key per kind.
    /// </summary>
    /// <param name="scope">The scope, such as a tenant id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the scope has its keys.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="scope" /> is too long or is not portable key text.</exception>
    Task EnsureAsync(string scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the next version of the scope's key of <paramref name="kind" />, which encrypts every new value from then
    /// on. Older versions keep decrypting; <see cref="FieldDecryption.KeyIsCurrent" /> reports the values a
    /// re-encryption job still has to rewrite.
    /// </summary>
    /// <remarks>
    /// Other hosts keep encrypting with the previous version until their cached active key expires
    /// (<c>EncryptionOptions.KeyCacheDuration</c>). Both versions decrypt, so nothing is lost meanwhile.
    /// </remarks>
    /// <param name="scope">The scope, such as a tenant id.</param>
    /// <param name="kind">The kind of key to rotate.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The id of the scope's active key of <paramref name="kind" /> after the call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="scope" /> is too long or is not portable key text, or <paramref name="kind" /> is undefined.
    /// </exception>
    Task<Guid> RotateAsync(string scope, DataKeyKind kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-wraps every stored data key that the key-encryption key's current version did not wrap, after the
    /// key-encryption key was rotated. The data keys and every encrypted value stay unchanged.
    /// </summary>
    /// <remarks>
    /// Keep the old key-encryption-key versions enabled until this reports 0. A deployment runs it as an operator
    /// step, never at host start.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the operation; keys already re-wrapped stay re-wrapped.</param>
    /// <returns>The number of keys re-wrapped.</returns>
    Task<int> RewrapAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every key of <paramref name="scope" />, so its encrypted values and blind indexes can no longer be read
    /// or linked (crypto-shredding).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This host forgets the keys at once; another host forgets them when its cached copy expires
    /// (<c>EncryptionOptions.KeyCacheDuration</c>). A later encryption for the scope creates a new key.
    /// </para>
    /// <para>
    /// A database backup taken before the shred still holds the wrapped keys, and restoring it while the
    /// key-encryption key exists recovers the data. Shredding is complete once backup retention passes.
    /// </para>
    /// </remarks>
    /// <param name="scope">The scope, such as a tenant id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of keys deleted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="scope" /> is too long or is not portable key text.</exception>
    Task<int> ShredAsync(string scope, CancellationToken cancellationToken = default);
}
