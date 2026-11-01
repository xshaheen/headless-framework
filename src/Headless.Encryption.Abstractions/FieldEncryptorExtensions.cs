// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using Headless.Checks;

namespace Headless.Encryption;

/// <summary>Encrypts and decrypts text through <see cref="IFieldEncryptor" />, as UTF-8.</summary>
[PublicAPI]
public static class FieldEncryptorExtensions
{
    /// <summary>Encrypts <paramref name="plaintext" /> as UTF-8 under the active data key of the context's scope.</summary>
    /// <param name="encryptor">The encryptor.</param>
    /// <param name="plaintext">The text to encrypt.</param>
    /// <param name="context">The purpose, scope, and optional binding the value is bound to.</param>
    /// <param name="cancellationToken">Cancels the key lookup.</param>
    /// <returns>The ciphertext frame.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="encryptor" />, <paramref name="plaintext" />, or <paramref name="context" /> is <see langword="null" />.
    /// </exception>
    public static async ValueTask<EncryptedValue> EncryptAsync(
        this IFieldEncryptor encryptor,
        string plaintext,
        FieldContext context,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(encryptor);
        Argument.IsNotNull(plaintext);

        var bytes = Encoding.UTF8.GetBytes(plaintext);

        try
        {
            return await encryptor.EncryptAsync(bytes, context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>Decrypts <paramref name="value" /> and decodes the plaintext as UTF-8.</summary>
    /// <param name="encryptor">The encryptor.</param>
    /// <param name="value">The ciphertext frame.</param>
    /// <param name="context">The same purpose, scope, and binding the value was encrypted with.</param>
    /// <param name="cancellationToken">Cancels the key lookup.</param>
    /// <returns>The plaintext. Use <see cref="IFieldEncryptor.DecryptAsync" /> to also learn whether the key is current.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="encryptor" /> or <paramref name="context" /> is <see langword="null" />.</exception>
    /// <exception cref="FieldDecryptionException">The value cannot be decrypted with this context.</exception>
    public static async ValueTask<string> DecryptStringAsync(
        this IFieldEncryptor encryptor,
        EncryptedValue value,
        FieldContext context,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(encryptor);

        var decryption = await encryptor.DecryptAsync(value, context, cancellationToken).ConfigureAwait(false);

        try
        {
            return decryption.GetString();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decryption.Plaintext);
        }
    }
}
