// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using FluentValidation;

namespace Headless.Abstractions;

[PublicAPI]
public interface IStringEncryptionService
{
    /// <summary>Encrypts a text.</summary>
    /// <param name="plainText">The text in plain format</param>
    /// <param name="passPhrase">A phrase to use as the encryption key (optional, uses default if not provided)</param>
    /// <param name="salt">Salt value (optional, uses default if not provided)</param>
    /// <returns>Encrypted text</returns>
    [return: NotNullIfNotNull(nameof(plainText))]
    string? Encrypt(string? plainText, string? passPhrase = null, byte[]? salt = null);

    /// <summary>Decrypts a text that is encrypted by the <see cref="Encrypt"/> method.</summary>
    /// <param name="cipherText">The text in encrypted format</param>
    /// <param name="passPhrase">A phrase to use as the encryption key (optional, uses default if not provided)</param>
    /// <param name="salt">Salt value (optional, uses default if not provided)</param>
    /// <returns>Decrypted text</returns>
    [return: NotNullIfNotNull(nameof(cipherText))]
    string? Decrypt(string? cipherText, string? passPhrase = null, byte[]? salt = null);
}

#region Options

/// <summary>Options used by <see cref="IStringEncryptionService"/>.</summary>
[PublicAPI]
public sealed class StringEncryptionOptions
{
    /// <summary>This constant is used to determine the key size of the encryption algorithm. Default value: 256.</summary>
    public int KeySize { get; set; } = 256;

    /// <summary>Default password to encrypt/decrypt texts.</summary>
    public required string DefaultPassPhrase { get; set; }

    /// <summary>
    /// This constant string is used as a "salt" value for the PasswordDeriveBytes function calls.
    /// This size of the IV (in bytes) must = (<see cref="KeySize"/> / 16).  Default <see cref="KeySize"/> is 256,
    /// so the IV must be 16 bytes long.
    /// </summary>
    public required byte[] InitVectorBytes { get; set; }

    /// <summary>Salt value for encryption.</summary>
    public required byte[] DefaultSalt { get; set; }
}

public sealed class StringEncryptionOptionsValidator : AbstractValidator<StringEncryptionOptions>
{
    public StringEncryptionOptionsValidator()
    {
        RuleFor(x => x.KeySize).GreaterThan(0);
        RuleFor(x => x.DefaultPassPhrase).NotEmpty();
        RuleFor(x => x.InitVectorBytes).NotEmpty().Must((settings, iv) => iv.Length == settings.KeySize / 16);
        RuleFor(x => x.DefaultSalt).NotEmpty();
    }
}

#endregion

#region Implementation

#pragma warning disable CA5401 // CA5401: Do not use CreateEncryptor with non-default IV
#pragma warning disable CA5379 // CA5379: Ensure key derivation function algorithm is sufficiently strong
public sealed class StringEncryptionService(StringEncryptionOptions options) : IStringEncryptionService
{
    private const int _Iterations = 100_000;
    private readonly HashAlgorithmName _hashAlgorithm = HashAlgorithmName.SHA256;

    public string? Encrypt(string? plainText, string? passPhrase = null, byte[]? salt = null)
    {
        if (plainText is null)
        {
            return null;
        }

        passPhrase ??= options.DefaultPassPhrase;
        salt ??= options.DefaultSalt;

        var plainTextBytes = Encoding.UTF8.GetBytes(plainText);

        var keyBytes = Rfc2898DeriveBytes.Pbkdf2(passPhrase, salt, _Iterations, _hashAlgorithm, options.KeySize / 8);

        using var symmetricKey = Aes.Create();
        symmetricKey.Mode = CipherMode.CBC;

        using var encryptor = symmetricKey.CreateEncryptor(keyBytes, options.InitVectorBytes);
        using var memoryStream = new MemoryStream();
        using var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write);
        cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length);
        cryptoStream.FlushFinalBlock();

        var cipherTextBytes = memoryStream.ToArray();

        return Convert.ToBase64String(cipherTextBytes);
    }

    public string? Decrypt(string? cipherText, string? passPhrase = null, byte[]? salt = null)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return null;
        }

        passPhrase ??= options.DefaultPassPhrase;
        salt ??= options.DefaultSalt;

        var cipherTextBytes = Convert.FromBase64String(cipherText);

        var keyBytes = Rfc2898DeriveBytes.Pbkdf2(
            password: passPhrase,
            salt: salt,
            iterations: _Iterations,
            hashAlgorithm: _hashAlgorithm,
            outputLength: options.KeySize / 8
        );

        using var symmetricKey = Aes.Create();
        symmetricKey.Mode = CipherMode.CBC;

        using var decryptor = symmetricKey.CreateDecryptor(keyBytes, options.InitVectorBytes);
        using var memoryStream = new MemoryStream(cipherTextBytes);
        using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);

        var plainTextBytes = new byte[cipherTextBytes.Length];
        var totalReadCount = 0;

        while (totalReadCount < cipherTextBytes.Length)
        {
            var buffer = new byte[cipherTextBytes.Length];
            var readCount = cryptoStream.Read(buffer, 0, buffer.Length);

            if (readCount == 0)
            {
                break;
            }

            for (var i = 0; i < readCount; i++)
            {
                plainTextBytes[i + totalReadCount] = buffer[i];
            }

            totalReadCount += readCount;
        }

        return Encoding.UTF8.GetString(plainTextBytes, 0, totalReadCount);
    }
}

#endregion
