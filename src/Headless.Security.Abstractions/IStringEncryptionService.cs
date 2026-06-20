// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>Encrypts and decrypts string values using AES-GCM authenticated encryption.</summary>
[PublicAPI]
public interface IStringEncryptionService
{
    /// <summary>Encrypts a plain text value.</summary>
    /// <param name="plainText">The value to encrypt.</param>
    /// <param name="passPhrase">An optional pass phrase override. Uses the configured default when omitted.</param>
    /// <param name="salt">An optional salt override. Uses the configured default when omitted.</param>
    /// <returns>
    /// The encrypted value (a Base64 string framing the random nonce, authentication tag, and cipher text), or
    /// <see langword="null" /> when <paramref name="plainText" /> is <see langword="null" />. A fresh random nonce is
    /// used per call, so encrypting the same value twice produces different cipher text.
    /// </returns>
    [return: NotNullIfNotNull(nameof(plainText))]
    string? Encrypt(string? plainText, string? passPhrase = null, byte[]? salt = null);

    /// <summary>Decrypts a value produced by <see cref="Encrypt" />.</summary>
    /// <param name="cipherText">The encrypted value to decrypt.</param>
    /// <param name="passPhrase">An optional pass phrase override. Uses the configured default when omitted.</param>
    /// <param name="salt">An optional salt override. Uses the configured default when omitted.</param>
    /// <returns>
    /// The decrypted value, or <see langword="null" /> when <paramref name="cipherText" /> is <see langword="null" />
    /// or empty.
    /// </returns>
    /// <exception cref="System.FormatException"><paramref name="cipherText" /> is not a valid Base64 string.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The cipher text is too short to contain a nonce and tag, or its authentication tag does not verify (the value
    /// was tampered with, or the pass phrase / salt does not match the one used to encrypt it).
    /// </exception>
    string? Decrypt(string? cipherText, string? passPhrase = null, byte[]? salt = null);
}
