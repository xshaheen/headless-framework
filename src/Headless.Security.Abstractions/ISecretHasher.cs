// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Security;

/// <summary>
/// Hashes secrets for storage and verifies them later — PINs, API-key secrets, recovery codes, or any secret that must
/// be checked but never read back.
/// </summary>
/// <remarks>
/// Every hash is a self-describing <see href="https://github.com/C2SP/C2SP/blob/main/phc-strings.md">PHC string</see>
/// (for example <c>$argon2id$v=19$m=19456,t=2,p=1$&lt;salt&gt;$&lt;hash&gt;</c>) that carries its algorithm, cost
/// parameters, and a per-record random salt, so the stored value is the only column an application needs. Changing
/// the configured algorithm or cost never invalidates existing records: <see cref="Verify" /> still accepts them and
/// returns an upgraded hash for the caller to persist.
/// </remarks>
[PublicAPI]
public interface ISecretHasher
{
    /// <summary>Hashes <paramref name="secret" /> with the configured algorithm and a new random salt.</summary>
    /// <param name="secret">The secret to hash. Must not be empty or longer than the configured maximum length.</param>
    /// <returns>The PHC-encoded hash. Hashing the same secret twice returns two different encodings.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="secret" /> is empty, longer than <see cref="SecretHasherOptions.MaxSecretLength" />, or is not
    /// valid UTF-16 (it contains a lone surrogate).
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No implementation is registered for the configured <see cref="SecretHasherOptions.Algorithm" />.
    /// </exception>
    string Hash(ReadOnlySpan<char> secret);

    /// <summary>Verifies <paramref name="secret" /> against a stored PHC-encoded hash in constant time.</summary>
    /// <param name="secret">The secret to check.</param>
    /// <param name="encoded">The stored hash previously returned by <see cref="Hash" />.</param>
    /// <returns>
    /// The verification result. A malformed, unknown, or out-of-bounds <paramref name="encoded" /> value fails rather
    /// than throws. After a success, <see cref="SecretVerification.Rehashed" /> carries an upgraded hash when the
    /// stored algorithm or cost is below the configured one.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="encoded" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    /// Verification succeeded and needs an upgrade, but no implementation is registered for the configured
    /// <see cref="SecretHasherOptions.Algorithm" />.
    /// </exception>
    SecretVerification Verify(ReadOnlySpan<char> secret, string encoded);
}
