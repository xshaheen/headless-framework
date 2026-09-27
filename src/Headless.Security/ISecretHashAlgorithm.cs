// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Security;

/// <summary>
/// One secret-hashing algorithm, identified by its PHC id. <see cref="ISecretHasher" /> dispatches verification to the
/// algorithm named in the stored hash, so several algorithms can be registered at once and old hashes keep verifying
/// after the configured algorithm changes.
/// </summary>
/// <remarks>
/// <para>
/// Register implementations with <c>TryAddEnumerable</c> as singletons. Read <c>IOptions&lt;SecretHasherOptions&gt;.Value</c>
/// at call time rather than in the constructor: the options validator runs while options are being built, and
/// resolving options from a constructor can re-enter that construction.
/// </para>
/// <para>
/// The hasher owns secret encoding, the constant-time comparison, and rehash assembly. An implementation only derives
/// bytes and judges parameters, and must never throw for any <see cref="PhcString" /> it is handed: stored hashes are
/// untrusted input.
/// </para>
/// </remarks>
[PublicAPI]
public interface ISecretHashAlgorithm
{
    /// <summary>Gets the PHC identifier this algorithm produces and verifies, such as <c>argon2id</c>.</summary>
    string Id { get; }

    /// <summary>Hashes <paramref name="secret" /> with the configured parameters and a new random salt.</summary>
    /// <param name="secret">The UTF-8 encoded secret.</param>
    /// <returns>The PHC-encoded hash.</returns>
    string Hash(ReadOnlySpan<byte> secret);

    /// <summary>
    /// Derives the hash of <paramref name="secret" /> under the salt and parameters of <paramref name="encoded" />.
    /// </summary>
    /// <param name="secret">The UTF-8 encoded secret.</param>
    /// <param name="encoded">The parsed stored hash. Its <see cref="PhcString.Id" /> equals <see cref="Id" />.</param>
    /// <param name="destination">Receives the derived bytes. Its length equals <c>encoded.Hash.Length</c>.</param>
    /// <returns>
    /// <see langword="false" /> without deriving anything when the encoding's version, parameters, or lengths are
    /// outside what this algorithm accepts, or when the derivation itself fails.
    /// </returns>
    bool TryComputeHash(ReadOnlySpan<byte> secret, PhcString encoded, Span<byte> destination);

    /// <summary>Determines whether a verified hash should be replaced by one with the configured parameters.</summary>
    /// <param name="encoded">A stored hash that <see cref="TryComputeHash" /> accepted.</param>
    /// <returns><see langword="true" /> when any stored cost or length parameter is below the configured value.</returns>
    bool NeedsRehash(PhcString encoded);
}
