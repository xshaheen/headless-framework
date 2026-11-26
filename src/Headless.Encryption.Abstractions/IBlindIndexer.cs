// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Encryption;

/// <summary>
/// Computes blind indexes: deterministic keyed hashes (HMAC-SHA256) of a value, stored next to its encrypted form so
/// the application can find rows by exact match without storing or querying the plaintext.
/// </summary>
/// <remarks>
/// <para>
/// The key is the scope's <see cref="DataKeyKind.Lookup" /> data key, never the value-encryption key. The same value,
/// purpose, and scope always hash the same; a different purpose or scope hashes differently.
/// </para>
/// <para>
/// The hash covers the exact UTF-8 bytes of the value. Normalize and validate the value first: two spellings of one
/// value hash differently. The input value and key material never appear in a log record or an exception message.
/// </para>
/// <para>
/// A context with no scope uses the current tenant; with no current tenant both methods throw
/// <c>MissingTenantContextException</c> (<c>Headless.MultiTenancy</c>) and never fall back to the host key.
/// </para>
/// </remarks>
[PublicAPI]
public interface IBlindIndexer
{
    /// <summary>
    /// Hashes <paramref name="value" /> under the scope's active lookup key, for writing. Creates the scope's lookup key
    /// on first use.
    /// </summary>
    /// <param name="context">The purpose, scope, and hash length.</param>
    /// <param name="value">The normalized value to hash.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The hash and the key version that produced it; store both.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context" /> or <paramref name="value" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="value" /> contains a lone surrogate, which UTF-8 cannot encode.</exception>
    /// <exception cref="DataKeyScopeShreddedException">The scope was shredded.</exception>
    ValueTask<BlindIndex> ComputeAsync(
        BlindIndexContext context,
        string value,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Hashes <paramref name="value" /> again under the scope's active lookup key when its stored hash used an older
    /// version, for a rehash job after <c>IDataKeyManager.RotateAsync</c>.
    /// </summary>
    /// <remarks>
    /// A stored version at or above the active one returns <see langword="null" />: the row is current, or another host
    /// already rehashed it under a version this host's cached active key has not reached yet, and rewriting it would
    /// move it backwards.
    /// </remarks>
    /// <param name="context">The purpose, scope, and hash length the stored hash was computed with.</param>
    /// <param name="value">The normalized value, decrypted from the row.</param>
    /// <param name="storedKeyVersion">The <see cref="BlindIndex.KeyVersion" /> stored with the row's hash.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The hash to store under the active version, or <see langword="null" /> when the stored one is current.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context" /> or <paramref name="value" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="value" /> contains a lone surrogate, which UTF-8 cannot encode.</exception>
    /// <exception cref="DataKeyScopeShreddedException">The scope was shredded.</exception>
    ValueTask<BlindIndex?> RehashAsync(
        BlindIndexContext context,
        string value,
        int storedKeyVersion,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Hashes <paramref name="value" /> under every stored lookup-key version of the scope, newest first, for querying
    /// (<c>WHERE hash = ANY(@candidates)</c>). Never creates a key.
    /// </summary>
    /// <param name="context">The purpose, scope, and hash length.</param>
    /// <param name="value">The normalized value to hash.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// One hash per key version, newest first; empty when the scope has no lookup key yet or was shredded, so the query
    /// matches nothing.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context" /> or <paramref name="value" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="value" /> contains a lone surrogate, which UTF-8 cannot encode.</exception>
    ValueTask<IReadOnlyList<BlindIndex>> ComputeCandidatesAsync(
        BlindIndexContext context,
        string value,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Hashes each of <paramref name="values" /> under the scope's active lookup key, for writing a batch. Leases the key
    /// once, so every hash uses the same version. Creates the scope's lookup key on first use.
    /// </summary>
    /// <remarks>
    /// Each result equals what <see cref="ComputeAsync" /> returns for the same value under the same key version. Every
    /// value is checked before a key is read or created, so an invalid value leaves the store unchanged.
    /// </remarks>
    /// <param name="context">The purpose, scope, and hash length.</param>
    /// <param name="values">The normalized values to hash; duplicates are allowed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// One hash per value, in the order of <paramref name="values" />, all under one key version; empty for an empty
    /// list, which reads no key.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context" /> or <paramref name="values" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">A value is <see langword="null" /> or contains a lone surrogate, which UTF-8 cannot encode.</exception>
    /// <exception cref="DataKeyScopeShreddedException">The scope was shredded.</exception>
    ValueTask<IReadOnlyList<BlindIndex>> ComputeAllAsync(
        BlindIndexContext context,
        IReadOnlyList<string> values,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Hashes each of <paramref name="values" /> under every stored lookup-key version of the scope, newest first, for
    /// querying a batch. Reads the scope's versions from the store once per call. Never creates a key.
    /// </summary>
    /// <remarks>
    /// Each entry equals what <see cref="ComputeCandidatesAsync" /> returns for the same value, and every entry uses the
    /// same versions. Every value is checked before the store is read.
    /// </remarks>
    /// <param name="context">The purpose, scope, and hash length.</param>
    /// <param name="values">The normalized values to hash; duplicates are allowed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// One candidate list per value, in the order of <paramref name="values" />, each with one hash per key version,
    /// newest first. Each list is empty when the scope has no lookup key yet or was shredded. Empty for an empty list,
    /// which reads nothing.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context" /> or <paramref name="values" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">A value is <see langword="null" /> or contains a lone surrogate, which UTF-8 cannot encode.</exception>
    ValueTask<IReadOnlyList<IReadOnlyList<BlindIndex>>> ComputeAllCandidatesAsync(
        BlindIndexContext context,
        IReadOnlyList<string> values,
        CancellationToken cancellationToken = default
    );
}
