// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using Headless.Checks;

namespace Headless.Idempotency;

/// <summary>
/// A versioned digest of the request an idempotency key stands for. Two admissions of one key must carry the same
/// fingerprint; a different one means the key was reused for a different request, which is a conflict.
/// </summary>
/// <remarks>
/// <para>
/// The algorithm tag is stored beside the digest so the hashing can change later without misreading old records: a
/// stored fingerprint whose algorithm this version does not know is refused, never recomputed, because the request
/// that produced it is gone and a guess would either replay a wrong result or reject a legitimate retry.
/// </para>
/// <para>
/// <see cref="V1" /> is SHA-256 over a caller-supplied canonical payload. The caller owns canonicalization: two
/// requests the caller considers equal must produce identical payload bytes (for example, sorted fields and a fixed
/// number format).
/// </para>
/// </remarks>
[PublicAPI]
public sealed class IdempotencyFingerprint : IEquatable<IdempotencyFingerprint>
{
    /// <summary>The algorithm tag of SHA-256 over the caller's canonical payload.</summary>
    public const string V1 = "v1";

    private readonly byte[] _hash;

    /// <summary>Creates a fingerprint from a stored or externally computed algorithm tag and digest.</summary>
    /// <param name="algorithm">The algorithm tag, such as <see cref="V1" />.</param>
    /// <param name="hash">The digest bytes; copied.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="algorithm" /> is empty or whitespace, or <paramref name="hash" /> is empty.
    /// </exception>
    public IdempotencyFingerprint(string algorithm, ReadOnlySpan<byte> hash)
    {
        Argument.IsNotNullOrWhiteSpace(algorithm);

        if (hash.IsEmpty)
        {
            throw new ArgumentException("A fingerprint digest must not be empty.", nameof(hash));
        }

        Algorithm = algorithm;
        _hash = hash.ToArray();
    }

    /// <summary>Gets the algorithm tag that produced <see cref="Hash" />.</summary>
    public string Algorithm { get; }

    /// <summary>Gets the digest bytes.</summary>
    public ReadOnlyMemory<byte> Hash => _hash;

    /// <summary>Computes the current (<see cref="V1" />) fingerprint of a canonical request payload.</summary>
    /// <param name="canonicalPayload">The request's canonical bytes.</param>
    /// <returns>The fingerprint.</returns>
    public static IdempotencyFingerprint Compute(ReadOnlySpan<byte> canonicalPayload)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(canonicalPayload, hash);

        return new IdempotencyFingerprint(V1, hash);
    }

    /// <summary>Computes the current (<see cref="V1" />) fingerprint of a canonical request string, as UTF-8.</summary>
    /// <param name="canonicalPayload">The request's canonical text.</param>
    /// <returns>The fingerprint.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="canonicalPayload" /> is <see langword="null" />.</exception>
    public static IdempotencyFingerprint Compute(string canonicalPayload)
    {
        Argument.IsNotNull(canonicalPayload);

        return Compute(Encoding.UTF8.GetBytes(canonicalPayload));
    }

    /// <summary>Whether this version knows how to compare fingerprints of <paramref name="algorithm" />.</summary>
    /// <param name="algorithm">The algorithm tag.</param>
    /// <returns><see langword="true" /> for a known tag.</returns>
    public static bool IsKnownAlgorithm(string? algorithm)
    {
        return string.Equals(algorithm, V1, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether this fingerprint identifies the same request as <paramref name="stored" />, the fingerprint an existing
    /// record carries.
    /// </summary>
    /// <param name="stored">The stored fingerprint.</param>
    /// <returns><see langword="true" /> when both name the same request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stored" /> is <see langword="null" />.</exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="stored" /> was produced by an algorithm this version does not know, so it cannot be compared.
    /// </exception>
    public bool Matches(IdempotencyFingerprint stored)
    {
        Argument.IsNotNull(stored);

        if (!IsKnownAlgorithm(stored.Algorithm))
        {
            throw new NotSupportedException(
                $"The stored idempotency fingerprint uses algorithm '{stored.Algorithm}', which this version does "
                    + "not know. It cannot be recomputed because the original request is gone; upgrade to a version "
                    + "that knows the algorithm, or let the record's retention end."
            );
        }

        return Equals(stored);
    }

    /// <inheritdoc />
    public bool Equals(IdempotencyFingerprint? other)
    {
        return other is not null
            && string.Equals(Algorithm, other.Algorithm, StringComparison.Ordinal)
            && _hash.AsSpan().SequenceEqual(other._hash);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is IdempotencyFingerprint other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Algorithm, StringComparer.Ordinal);
        hash.AddBytes(_hash);

        return hash.ToHashCode();
    }

    /// <summary>Returns the fingerprint as <c>algorithm:lowercase-hex</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString()
    {
        return $"{Algorithm}:{Convert.ToHexStringLower(_hash)}";
    }
}
