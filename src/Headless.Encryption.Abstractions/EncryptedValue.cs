// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using Headless.Checks;

namespace Headless.Encryption;

/// <summary>
/// A field value encrypted by <see cref="IFieldEncryptor" />: one self-describing binary frame that names its data key,
/// so it can be stored in a single <c>bytea</c> or <c>varbinary(max)</c> column, or as text with
/// <see cref="ToBase64Url" />.
/// </summary>
/// <remarks>
/// <para>
/// The frame is: format version (1 byte, <c>0x01</c>), algorithm (1 byte, <c>0x01</c> = AES-256-GCM under an
/// HKDF-SHA256 purpose subkey), data-key id (16 bytes, big-endian UUID), nonce (12 bytes), ciphertext, and the
/// 16-byte authentication tag: <see cref="Overhead" /> bytes more than the plaintext.
/// </para>
/// <para>
/// Equality compares the frame bytes. Two encryptions of one plaintext differ (each has a fresh nonce), so equality
/// never says whether two plaintexts are equal; search by a blind index instead.
/// </para>
/// </remarks>
[PublicAPI]
public readonly struct EncryptedValue : IEquatable<EncryptedValue>
{
    /// <summary>The format version this release writes and reads.</summary>
    public const byte FormatVersion = 0x01;

    /// <summary>The length of the header that precedes the ciphertext: version, algorithm, key id, nonce.</summary>
    public const int HeaderLength = 30;

    /// <summary>The length of the authentication tag that ends the frame.</summary>
    public const int TagLength = 16;

    /// <summary>The number of bytes a frame adds to its plaintext.</summary>
    public const int Overhead = HeaderLength + TagLength;

    private const int _KeyIdOffset = 2;

    private readonly byte[]? _frame;

    private EncryptedValue(byte[] frame)
    {
        _frame = frame;
    }

    /// <summary>Gets whether this is the default value, which holds no frame.</summary>
    public bool IsDefault => _frame is null;

    /// <summary>Gets the frame's bytes; empty for the default value.</summary>
    public ReadOnlyMemory<byte> Frame => _frame;

    /// <summary>Gets the frame's length in bytes; 0 for the default value.</summary>
    public int Length => _frame?.Length ?? 0;

    /// <summary>Gets the algorithm byte. The encryptor refuses an algorithm it does not know.</summary>
    /// <exception cref="InvalidOperationException">This is the default value.</exception>
    public byte Algorithm => _Require()[1];

    /// <summary>Gets the id of the data key that encrypted the value.</summary>
    /// <exception cref="InvalidOperationException">This is the default value.</exception>
    public Guid KeyId => new(_Require().AsSpan(_KeyIdOffset, 16), bigEndian: true);

    /// <summary>Copies <paramref name="frame" /> into a value after checking it has a frame's shape.</summary>
    /// <param name="frame">The bytes read from storage.</param>
    /// <returns>The value.</returns>
    /// <exception cref="FormatException">
    /// <paramref name="frame" /> is shorter than <see cref="Overhead" /> bytes or its format version is unknown.
    /// </exception>
    public static EncryptedValue FromBytes(ReadOnlySpan<byte> frame)
    {
        return TryFromBytes(frame, out var value)
            ? value
            : throw new FormatException(
                $"The bytes are not an encrypted value: a frame is at least {Overhead} bytes and starts with format version {FormatVersion}."
            );
    }

    /// <summary>Copies <paramref name="frame" /> into a value when it has a frame's shape.</summary>
    /// <param name="frame">The bytes read from storage.</param>
    /// <param name="value">The value, or the default value when the bytes are not a frame.</param>
    /// <returns><see langword="true" /> when the bytes are a frame.</returns>
    public static bool TryFromBytes(ReadOnlySpan<byte> frame, out EncryptedValue value)
    {
        if (frame.Length < Overhead || frame[0] != FormatVersion)
        {
            value = default;

            return false;
        }

        value = new EncryptedValue(frame.ToArray());

        return true;
    }

    /// <summary>Reads a value written by <see cref="ToBase64Url" />.</summary>
    /// <param name="text">The unpadded base64url text.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException"><paramref name="text" /> is not base64url, or its bytes are not a frame.</exception>
    public static EncryptedValue Parse(string text)
    {
        Argument.IsNotNull(text);

        return TryParse(text, out var value)
            ? value
            : throw new FormatException("The text is not a base64url-encoded encrypted value.");
    }

    /// <summary>Reads a value written by <see cref="ToBase64Url" />, when the text is one.</summary>
    /// <param name="text">The unpadded base64url text.</param>
    /// <param name="value">The value, or the default value when the text is not one.</param>
    /// <returns><see langword="true" /> when the text is a base64url-encoded frame.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, out EncryptedValue value)
    {
        value = default;

        if (text is null || !Base64Url.IsValid(text, out var length) || length < Overhead)
        {
            return false;
        }

        var frame = new byte[length];

        if (
            !Base64Url.TryDecodeFromChars(text, frame, out var written)
            || written != length
            || frame[0] != FormatVersion
        )
        {
            return false;
        }

        value = new EncryptedValue(frame);

        return true;
    }

    /// <summary>Returns a copy of the frame's bytes, for storage.</summary>
    /// <returns>The frame; empty for the default value.</returns>
    public byte[] ToArray() => _frame is null ? [] : (byte[])_frame.Clone();

    /// <summary>Returns the frame as unpadded base64url text, for a column that must stay text.</summary>
    /// <returns>The text; empty for the default value.</returns>
    public string ToBase64Url() => _frame is null ? string.Empty : Base64Url.EncodeToString(_frame);

    /// <inheritdoc />
    public override string ToString() => ToBase64Url();

    /// <inheritdoc />
    public bool Equals(EncryptedValue other) => Frame.Span.SequenceEqual(other.Frame.Span);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EncryptedValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        if (_frame is null)
        {
            return 0;
        }

        // The tag is uniformly random per encryption, so its first bytes hash as well as the whole frame.
        return BinaryPrimitives.ReadInt32LittleEndian(_frame.AsSpan(_frame.Length - TagLength));
    }

    /// <summary>Compares two values by their frame bytes.</summary>
    public static bool operator ==(EncryptedValue left, EncryptedValue right) => left.Equals(right);

    /// <summary>Compares two values by their frame bytes.</summary>
    public static bool operator !=(EncryptedValue left, EncryptedValue right) => !left.Equals(right);

    private byte[] _Require() =>
        _frame ?? throw new InvalidOperationException("The default EncryptedValue holds no frame.");
}
