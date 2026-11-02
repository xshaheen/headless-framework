// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Encryption;

/// <summary>A blind-index hash and the version of the scope's lookup key that produced it.</summary>
/// <param name="keyVersion">The lookup-key version; store it in its own column to find the rows a rehash job still has to rewrite.</param>
/// <param name="hash">The hash, <see cref="BlindIndexContext.Length" /> bytes; store it in the indexed column.</param>
[PublicAPI]
public readonly struct BlindIndex(int keyVersion, byte[] hash) : IEquatable<BlindIndex>
{
    /// <summary>Gets the version of the scope's lookup key that produced <see cref="Hash" />.</summary>
    public int KeyVersion { get; } = keyVersion;

    /// <summary>Gets the hash bytes. The array is the caller's to store.</summary>
    public byte[] Hash { get; } = hash;

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
