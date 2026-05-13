// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>Encrypts and decrypts string values using the configured symmetric encryption settings.</summary>
[PublicAPI]
public interface IStringEncryptionService
{
    /// <summary>Encrypts a plain text value.</summary>
    /// <param name="plainText">The value to encrypt.</param>
    /// <param name="passPhrase">An optional pass phrase override. Uses the configured default when omitted.</param>
    /// <param name="salt">An optional salt override. Uses the configured default when omitted.</param>
    /// <returns>The encrypted value, or <see langword="null" /> when <paramref name="plainText" /> is <see langword="null" />.</returns>
    [return: NotNullIfNotNull(nameof(plainText))]
    string? Encrypt(string? plainText, string? passPhrase = null, byte[]? salt = null);

    /// <summary>Decrypts an encrypted string value.</summary>
    /// <param name="cipherText">The encrypted value to decrypt.</param>
    /// <param name="passPhrase">An optional pass phrase override. Uses the configured default when omitted.</param>
    /// <param name="salt">An optional salt override. Uses the configured default when omitted.</param>
    /// <returns>
    /// The decrypted value, or <see langword="null" /> when <paramref name="cipherText" /> is <see langword="null" />
    /// or empty.
    /// </returns>
    [return: NotNullIfNotNull(nameof(cipherText))]
    string? Decrypt(string? cipherText, string? passPhrase = null, byte[]? salt = null);
}
