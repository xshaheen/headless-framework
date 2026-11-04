// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using Headless.Checks;

namespace Headless.Encryption;

/// <summary>A blind-index hash and the version of the scope's lookup key that produced it.</summary>
/// <remarks>
/// Store <see cref="Hash" /> in a binary column, or the whole index as text with <see cref="ToBase64Url" />: unpadded
/// base64url of the key version (4 bytes, big-endian) followed by the hash. One input under one key version always
/// gives the same text, so an equality lookup on a text column works as it does on the hash bytes.
/// </remarks>
/// <param name="keyVersion">The lookup-key version; store it in its own column to find the rows a rehash job still has to rewrite.</param>
/// <param name="hash">The hash, <see cref="BlindIndexContext.Length" /> bytes; store it in the indexed column.</param>
[PublicAPI]
public readonly struct BlindIndex(int keyVersion, byte[] hash) : IEquatable<BlindIndex>
{
    /// <summary>Gets the version of the scope's lookup key that produced <see cref="Hash" />.</summary>
    public int KeyVersion { get; } = keyVersion;

    /// <summary>Gets the hash bytes. The array is the caller's to store.</summary>
    public byte[] Hash { get; } = hash;

    private const int _VersionLength = sizeof(int);

    /// <summary>Returns the index as unpadded base64url text, for a column that must stay text.</summary>
    /// <returns>The text; empty for the default value, which holds no hash.</returns>
    public string ToBase64Url()
    {
        if (Hash is null)
        {
            return string.Empty;
        }

        var frame = new byte[_VersionLength + Hash.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, KeyVersion);
        Hash.CopyTo(frame.AsSpan(_VersionLength));

        return Base64Url.EncodeToString(frame);
    }

    /// <inheritdoc />
    public override string ToString() => ToBase64Url();

    /// <summary>Reads an index written by <see cref="ToBase64Url" />.</summary>
    /// <param name="text">The unpadded base64url text.</param>
    /// <returns>The index.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">
    /// <paramref name="text" /> is not base64url, or its hash is not <see cref="BlindIndexContext.MinLength" /> to
    /// <see cref="BlindIndexContext.MaxLength" /> bytes.
    /// </exception>
    public static BlindIndex Parse(string text)
    {
        Argument.IsNotNull(text);

        return TryParse(text, out var index)
            ? index
            : throw new FormatException("The text is not a base64url-encoded blind index.");
    }

    /// <summary>Reads an index written by <see cref="ToBase64Url" />, when the text is one.</summary>
    /// <param name="text">The unpadded base64url text.</param>
    /// <param name="index">The index, or the default value when the text is not one.</param>
    /// <returns><see langword="true" /> when the text is a base64url-encoded blind index.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, out BlindIndex index)
    {
        index = default;

        if (
            text is null
            || !Base64Url.IsValid(text, out var length)
            || length < _VersionLength + BlindIndexContext.MinLength
            || length > _VersionLength + BlindIndexContext.MaxLength
        )
        {
            return false;
        }

        var frame = new byte[length];

        if (!Base64Url.TryDecodeFromChars(text, frame, out var written) || written != length)
        {
            return false;
        }

        index = new BlindIndex(BinaryPrimitives.ReadInt32BigEndian(frame), frame.AsSpan(_VersionLength).ToArray());

        return true;
    }

    /// <summary>Compares the key version and the hash bytes.</summary>
    /// <param name="other">The other blind index.</param>
    /// <returns><see langword="true" /> when both have the same key version and hash bytes.</returns>
    public bool Equals(BlindIndex other) =>
        KeyVersion == other.KeyVersion && (Hash ?? []).AsSpan().SequenceEqual(other.Hash ?? []);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BlindIndex other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(KeyVersion);
        hashCode.AddBytes(Hash ?? []);

        return hashCode.ToHashCode();
    }

    /// <summary>Compares two blind indexes by key version and hash bytes.</summary>
    /// <param name="left">The first blind index.</param>
    /// <param name="right">The second blind index.</param>
    /// <returns><see langword="true" /> when they are equal.</returns>
    public static bool operator ==(BlindIndex left, BlindIndex right) => left.Equals(right);

    /// <summary>Compares two blind indexes by key version and hash bytes.</summary>
    /// <param name="left">The first blind index.</param>
    /// <param name="right">The second blind index.</param>
    /// <returns><see langword="true" /> when they differ.</returns>
    public static bool operator !=(BlindIndex left, BlindIndex right) => !left.Equals(right);
}
