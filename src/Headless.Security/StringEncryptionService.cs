// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Abstractions;

namespace Headless;

/// <summary>Encrypts and decrypts strings using AES-GCM authenticated encryption with PBKDF2 key derivation.</summary>
internal sealed class StringEncryptionService : IStringEncryptionService
{
    private const int _NonceSize = 12; // AES-GCM standard nonce size (AesGcm.NonceByteSizes.MaxSize).
    private const int _TagSize = 16; // AES-GCM authentication tag size (AesGcm.TagByteSizes.MaxSize).
    private static readonly HashAlgorithmName _HashAlgorithm = HashAlgorithmName.SHA256;

    private readonly string _defaultPassPhrase;
    private readonly byte[] _defaultSalt;
    private readonly byte[] _defaultKeyBytes;
    private readonly int _keySizeInBytes;
    private readonly int _iterations;

    public StringEncryptionService(StringEncryptionOptions options)
    {
        // Defensive copies: the service is a singleton; copying prevents external mutation of the
        // options array from silently altering the cached salt after construction.
        _defaultPassPhrase = options.DefaultPassPhrase;
        _defaultSalt = [.. options.DefaultSalt];
        _iterations = options.Iterations;
        _keySizeInBytes = options.KeySize / 8;
        _defaultKeyBytes = _CreateKeyBytes(_defaultPassPhrase, _defaultSalt, _iterations, _keySizeInBytes);
    }

    /// <inheritdoc />
    public string? Encrypt(string? plainText, string? passPhrase = null, byte[]? salt = null)
    {
        if (plainText is null)
        {
            return null;
        }

        var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
        var keyBytes = _GetKeyBytes(passPhrase, salt);

        // A fresh random nonce per message ensures identical plaintexts never produce identical ciphertexts.
        var nonce = new byte[_NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var cipherTextBytes = new byte[plainTextBytes.Length];
        var tag = new byte[_TagSize];

        using (var aes = new AesGcm(keyBytes, _TagSize))
        {
            aes.Encrypt(nonce, plainTextBytes, cipherTextBytes, tag);
        }

        // Frame the output as: nonce || tag || cipher text.
        var result = new byte[_NonceSize + _TagSize + cipherTextBytes.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, _NonceSize);
        cipherTextBytes.CopyTo(result, _NonceSize + _TagSize);

        return Convert.ToBase64String(result);
    }

    /// <inheritdoc />
    public string? Decrypt(string? cipherText, string? passPhrase = null, byte[]? salt = null)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return null;
        }

        var allBytes = Convert.FromBase64String(cipherText);

        if (allBytes.Length < _NonceSize + _TagSize)
        {
            throw new CryptographicException(
                "The cipher text is too short to contain a valid nonce and authentication tag."
            );
        }

        var keyBytes = _GetKeyBytes(passPhrase, salt);

        var nonce = allBytes.AsSpan(0, _NonceSize);
        var tag = allBytes.AsSpan(_NonceSize, _TagSize);
        var cipherTextBytes = allBytes.AsSpan(_NonceSize + _TagSize);
        var plainTextBytes = new byte[cipherTextBytes.Length];

        using (var aes = new AesGcm(keyBytes, _TagSize))
        {
            // Throws AuthenticationTagMismatchException (a CryptographicException) when the cipher text was tampered
            // with or the key derived from the pass phrase / salt does not match the one used to encrypt it.
            aes.Decrypt(nonce, cipherTextBytes, tag, plainTextBytes);
        }

        return Encoding.UTF8.GetString(plainTextBytes);
    }

    private byte[] _GetKeyBytes(string? passPhrase, byte[]? salt)
    {
        if (passPhrase is null && salt is null)
        {
            return _defaultKeyBytes;
        }

        return _CreateKeyBytes(passPhrase ?? _defaultPassPhrase, salt ?? _defaultSalt, _iterations, _keySizeInBytes);
    }

    private static byte[] _CreateKeyBytes(string passPhrase, byte[] salt, int iterations, int keySizeInBytes)
    {
        return Rfc2898DeriveBytes.Pbkdf2(passPhrase, salt, iterations, _HashAlgorithm, keySizeInBytes);
    }
}
