// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Abstractions;

#pragma warning disable CA5401, CA5379
namespace Headless;

/// <summary>Encrypts and decrypts strings using configured symmetric encryption options.</summary>
public sealed class StringEncryptionService(StringEncryptionOptions options) : IStringEncryptionService
{
    private const int _Iterations = 100_000;
    private static readonly HashAlgorithmName _HashAlgorithm = HashAlgorithmName.SHA256;
    private readonly string _defaultPassPhrase = options.DefaultPassPhrase;
    private readonly byte[] _defaultSalt = options.DefaultSalt;
    private readonly byte[] _defaultKeyBytes = _CreateKeyBytes(
        options.DefaultPassPhrase,
        options.DefaultSalt,
        options.KeySize / 8
    );
    private readonly byte[] _initVectorBytes = options.InitVectorBytes;
    private readonly int _keySizeInBytes = options.KeySize / 8;

    /// <inheritdoc />
    public string? Encrypt(string? plainText, string? passPhrase = null, byte[]? salt = null)
    {
        if (plainText is null)
        {
            return null;
        }

        var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
        var keyBytes = _GetKeyBytes(passPhrase, salt);

        using var symmetricKey = Aes.Create();
        symmetricKey.Key = keyBytes;

        var cipherTextBytes = symmetricKey.EncryptCbc(plainTextBytes, _initVectorBytes);
        return Convert.ToBase64String(cipherTextBytes);
    }

    /// <inheritdoc />
    public string? Decrypt(string? cipherText, string? passPhrase = null, byte[]? salt = null)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return null;
        }

        var cipherTextBytes = Convert.FromBase64String(cipherText);
        var keyBytes = _GetKeyBytes(passPhrase, salt);

        using var symmetricKey = Aes.Create();
        symmetricKey.Key = keyBytes;

        var plainTextBytes = symmetricKey.DecryptCbc(cipherTextBytes, _initVectorBytes);
        return Encoding.UTF8.GetString(plainTextBytes);
    }

    private byte[] _GetKeyBytes(string? passPhrase, byte[]? salt)
    {
        if (passPhrase is null && salt is null)
        {
            return _defaultKeyBytes;
        }

        return _CreateKeyBytes(passPhrase ?? _defaultPassPhrase, salt ?? _defaultSalt, _keySizeInBytes);
    }

    private static byte[] _CreateKeyBytes(string passPhrase, byte[] salt, int keySizeInBytes)
    {
        return Rfc2898DeriveBytes.Pbkdf2(passPhrase, salt, _Iterations, _HashAlgorithm, keySizeInBytes);
    }
}
