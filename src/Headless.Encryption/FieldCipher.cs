// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Headless.Encryption;

/// <summary>
/// Seals and opens one <see cref="EncryptedValue" /> frame: AES-256-GCM under a subkey derived from the data key by
/// HKDF-SHA256 per purpose, with the frame header, scope, purpose, and binding as authenticated data.
/// </summary>
/// <remarks>
/// <para>
/// One subkey per purpose keeps each column's random-nonce count under its own GCM key, against NIST SP 800-38D's
/// limit of 2^32 random-nonce invocations per key; a data-key rotation resets every count.
/// </para>
/// <para>
/// The authenticated data is the 18 header bytes followed by the scope, purpose, and binding, each with a 4-byte
/// big-endian length prefix, so no two splits of the fields give the same bytes, and a changed key id or algorithm
/// byte fails the tag check.
/// </para>
/// </remarks>
internal static class FieldCipher
{
    /// <summary>AES-256-GCM under an HKDF-SHA256 purpose subkey.</summary>
    public const byte AesGcmHkdfSha256 = 0x01;

    public const int KeyLength = 32;

    private const int _AuthenticatedHeaderLength = 18;
    private const int _NonceOffset = 18;
    private const int _NonceLength = 12;

    // Scope, purpose, and binding are short identifiers, so the derivation info and the authenticated data fit on the
    // stack for every realistic call; a longer one falls back to the heap.
    private const int _StackBufferLength = 256;

    private static ReadOnlySpan<byte> SubkeyInfoPrefix => "headless/fe/v1\0"u8;

    public static byte[] Seal(
        ReadOnlySpan<byte> dataKey,
        Guid keyId,
        string scope,
        string purpose,
        ReadOnlySpan<byte> binding,
        ReadOnlySpan<byte> plaintext
    )
    {
        var frame = new byte[EncryptedValue.Overhead + plaintext.Length];
        frame[0] = EncryptedValue.FormatVersion;
        frame[1] = AesGcmHkdfSha256;
        keyId.TryWriteBytes(frame.AsSpan(2, 16), bigEndian: true, out _);

        var nonce = frame.AsSpan(_NonceOffset, _NonceLength);
        RandomNumberGenerator.Fill(nonce);

        var aadLength = _AuthenticatedDataLength(scope, purpose, binding);
        var aad = aadLength <= _StackBufferLength ? stackalloc byte[_StackBufferLength] : new byte[aadLength];
        aad = aad[..aadLength];
        _WriteAuthenticatedData(aad, frame.AsSpan(0, _AuthenticatedHeaderLength), scope, purpose, binding);
        Span<byte> subkey = stackalloc byte[KeyLength];

        try
        {
            _DeriveSubkey(dataKey, purpose, subkey);

            using var aes = new AesGcm(subkey, EncryptedValue.TagLength);
            aes.Encrypt(
                nonce,
                plaintext,
                frame.AsSpan(EncryptedValue.HeaderLength, plaintext.Length),
                frame.AsSpan(EncryptedValue.HeaderLength + plaintext.Length),
                aad
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(subkey);
        }

        return frame;
    }

    /// <summary>Opens <paramref name="frame" />, or returns <see langword="null" /> when its tag does not verify.</summary>
    public static byte[]? Open(
        ReadOnlySpan<byte> dataKey,
        ReadOnlySpan<byte> frame,
        string scope,
        string purpose,
        ReadOnlySpan<byte> binding
    )
    {
        var ciphertextLength = frame.Length - EncryptedValue.Overhead;
        var plaintext = new byte[ciphertextLength];
        var aadLength = _AuthenticatedDataLength(scope, purpose, binding);
        var aad = aadLength <= _StackBufferLength ? stackalloc byte[_StackBufferLength] : new byte[aadLength];
        aad = aad[..aadLength];
        _WriteAuthenticatedData(aad, frame[.._AuthenticatedHeaderLength], scope, purpose, binding);
        Span<byte> subkey = stackalloc byte[KeyLength];

        try
        {
            _DeriveSubkey(dataKey, purpose, subkey);

            using var aes = new AesGcm(subkey, EncryptedValue.TagLength);
            aes.Decrypt(
                frame.Slice(_NonceOffset, _NonceLength),
                frame.Slice(EncryptedValue.HeaderLength, ciphertextLength),
                frame[(EncryptedValue.HeaderLength + ciphertextLength)..],
                plaintext,
                aad
            );

            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            // AesGcm already clears the destination on a failed tag; clear again so no path returns partial plaintext.
            CryptographicOperations.ZeroMemory(plaintext);

            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(subkey);
        }
    }

    private static void _DeriveSubkey(ReadOnlySpan<byte> dataKey, string purpose, Span<byte> subkey)
    {
        var infoLength = SubkeyInfoPrefix.Length + Encoding.UTF8.GetByteCount(purpose);
        var info = infoLength <= _StackBufferLength ? stackalloc byte[_StackBufferLength] : new byte[infoLength];
        info = info[..infoLength];
        SubkeyInfoPrefix.CopyTo(info);
        Encoding.UTF8.GetBytes(purpose, info[SubkeyInfoPrefix.Length..]);

        HKDF.DeriveKey(HashAlgorithmName.SHA256, dataKey, subkey, salt: [], info);
    }

    private static int _AuthenticatedDataLength(string scope, string purpose, ReadOnlySpan<byte> binding)
    {
        return _AuthenticatedHeaderLength
            + 12
            + Encoding.UTF8.GetByteCount(scope)
            + Encoding.UTF8.GetByteCount(purpose)
            + binding.Length;
    }

    private static void _WriteAuthenticatedData(
        Span<byte> aad,
        ReadOnlySpan<byte> header,
        string scope,
        string purpose,
        ReadOnlySpan<byte> binding
    )
    {
        var offset = header.Length;

        header.CopyTo(aad);
        var scopeLength = Encoding.UTF8.GetBytes(scope, aad[(offset + 4)..]);
        BinaryPrimitives.WriteInt32BigEndian(aad[offset..], scopeLength);
        offset += 4 + scopeLength;
        var purposeLength = Encoding.UTF8.GetBytes(purpose, aad[(offset + 4)..]);
        BinaryPrimitives.WriteInt32BigEndian(aad[offset..], purposeLength);
        offset += 4 + purposeLength;
        BinaryPrimitives.WriteInt32BigEndian(aad[offset..], binding.Length);
        binding.CopyTo(aad[(offset + 4)..]);
    }
}
