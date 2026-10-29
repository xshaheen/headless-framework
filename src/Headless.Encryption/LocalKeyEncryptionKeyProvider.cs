// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using Headless.Checks;

namespace Headless.Encryption;

/// <summary>
/// Wraps data keys with AES-256-GCM under a key the host holds (<see cref="LocalKeyEncryptionKeyOptions" />), binding
/// each wrapped key to its scope, id, and kind as authenticated data.
/// </summary>
/// <remarks>A wrapped key is the 12-byte nonce, the 32-byte ciphertext, and the 16-byte tag.</remarks>
internal sealed class LocalKeyEncryptionKeyProvider : IKeyEncryptionKeyProvider
{
    public const string ProviderName = "Local";

    private const int _NonceLength = 12;
    private const int _TagLength = 16;

    private readonly FrozenDictionary<string, byte[]> _keys;
    private readonly string _activeKeyId;

    public LocalKeyEncryptionKeyProvider(LocalKeyEncryptionKeyOptions options)
    {
        Argument.IsNotNull(options);

        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var (id, value) in options.Keys)
        {
            keys[id] = TryDecodeKey(value, out var key)
                ? key
                : throw new ArgumentException($"Local key '{id}' is not a 32-byte key in base64.", nameof(options));
        }

        _keys = keys.ToFrozenDictionary(StringComparer.Ordinal);
        _activeKeyId = _keys.ContainsKey(options.ActiveKeyId)
            ? options.ActiveKeyId
            : throw new ArgumentException("ActiveKeyId must name one of the configured local keys.", nameof(options));
    }

    public string Name => ProviderName;

    private static ReadOnlySpan<byte> AadPrefix => "headless/kek/local/v1\0"u8;

    public ValueTask<WrappedDataKey> WrapAsync(
        ReadOnlyMemory<byte> dataKey,
        KeyWrapContext context,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(context);

        var wrapped = new byte[_NonceLength + dataKey.Length + _TagLength];
        var nonce = wrapped.AsSpan(0, _NonceLength);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_keys[_activeKeyId], _TagLength);
        aes.Encrypt(
            nonce,
            dataKey.Span,
            wrapped.AsSpan(_NonceLength, dataKey.Length),
            wrapped.AsSpan(_NonceLength + dataKey.Length),
            _AuthenticatedData(context)
        );

        return ValueTask.FromResult(new WrappedDataKey { KekId = _activeKeyId, Ciphertext = wrapped });
    }

    public ValueTask<byte[]> UnwrapAsync(
        WrappedDataKey wrapped,
        KeyWrapContext context,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(wrapped);
        Argument.IsNotNull(context);

        if (!_keys.TryGetValue(wrapped.KekId, out var kek))
        {
            throw new CryptographicException(
                $"No local key-encryption key has id '{wrapped.KekId}'. Keep a retired key configured until "
                    + "IDataKeyManager.RewrapAsync reports 0."
            );
        }

        var ciphertext = wrapped.Ciphertext;

        if (ciphertext.Length <= _NonceLength + _TagLength)
        {
            throw new CryptographicException("The wrapped data key is too short to be a local AES-GCM wrap.");
        }

        var keyLength = ciphertext.Length - _NonceLength - _TagLength;
        var key = new byte[keyLength];

        using var aes = new AesGcm(kek, _TagLength);
        aes.Decrypt(
            ciphertext.AsSpan(0, _NonceLength),
            ciphertext.AsSpan(_NonceLength, keyLength),
            ciphertext.AsSpan(_NonceLength + keyLength),
            key,
            _AuthenticatedData(context)
        );

        return ValueTask.FromResult(key);
    }

    internal static bool TryDecodeKey(string? base64, out byte[] key)
    {
        key = [];

        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        var buffer = new byte[(base64.Length * 3 / 4) + 3];

        if (!Convert.TryFromBase64String(base64, buffer, out var written) || written != 32)
        {
            CryptographicOperations.ZeroMemory(buffer);

            return false;
        }

        key = buffer[..32];
        CryptographicOperations.ZeroMemory(buffer);

        return true;
    }

    private static byte[] _AuthenticatedData(KeyWrapContext context)
    {
        var scopeLength = Encoding.UTF8.GetByteCount(context.Scope);
        var aad = new byte[AadPrefix.Length + 16 + 1 + 4 + scopeLength];
        var offset = AadPrefix.Length;

        AadPrefix.CopyTo(aad);
        context.KeyId.TryWriteBytes(aad.AsSpan(offset, 16), bigEndian: true, out _);
        aad[offset + 16] = (byte)context.Kind;
        BinaryPrimitives.WriteInt32BigEndian(aad.AsSpan(offset + 17), scopeLength);
        Encoding.UTF8.GetBytes(context.Scope, aad.AsSpan(offset + 21));

        return aad;
    }
}
