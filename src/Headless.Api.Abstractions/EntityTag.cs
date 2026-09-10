// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Microsoft.Net.Http.Headers;

namespace Headless.Abstractions;

/// <summary>Represents one HTTP entity tag in its canonical quoted form.</summary>
/// <remarks>
/// An entity tag identifies a selected HTTP representation. Its opaque value is deliberately independent from
/// the database-specific concurrency token used to produce it.
/// </remarks>
[PublicAPI]
public sealed record EntityTag
{
    private EntityTag(string headerValue, string opaqueValue, bool isWeak)
    {
        HeaderValue = headerValue;
        OpaqueValue = opaqueValue;
        IsWeak = isWeak;
    }

    /// <summary>Gets the complete HTTP field value, including quotes and any weakness indicator.</summary>
    public string HeaderValue { get; }

    /// <summary>Gets the opaque value without quotes or a weakness indicator.</summary>
    public string OpaqueValue { get; }

    /// <summary>Gets whether this entity tag uses weak comparison semantics.</summary>
    public bool IsWeak { get; }

    /// <summary>Creates a strong entity tag from an opaque value.</summary>
    /// <param name="opaqueValue">The opaque value without quotes.</param>
    /// <returns>A strong entity tag.</returns>
    /// <exception cref="ArgumentException"><paramref name="opaqueValue"/> contains invalid entity-tag characters.</exception>
    public static EntityTag CreateStrong(string opaqueValue) => _Create(opaqueValue, isWeak: false);

    /// <summary>Creates a weak entity tag from an opaque value.</summary>
    /// <param name="opaqueValue">The opaque value without quotes.</param>
    /// <returns>A weak entity tag.</returns>
    /// <exception cref="ArgumentException"><paramref name="opaqueValue"/> contains invalid entity-tag characters.</exception>
    public static EntityTag CreateWeak(string opaqueValue) => _Create(opaqueValue, isWeak: true);

    /// <summary>Creates a strong entity tag whose opaque value is the Base64 encoding of <paramref name="value"/>.</summary>
    /// <param name="value">A non-empty binary representation version.</param>
    /// <returns>A strong entity tag.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public static EntityTag FromBytes(ReadOnlySpan<byte> value)
    {
        Argument.IsNotEmpty(value);
        return CreateStrong(Convert.ToBase64String(value));
    }

    /// <summary>
    /// Creates a strong entity tag from an unsigned 32-bit version encoded in network byte order.
    /// </summary>
    /// <param name="value">The representation version, such as PostgreSQL's <c>xmin</c>.</param>
    /// <returns>A strong entity tag.</returns>
    public static EntityTag FromUInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return FromBytes(bytes);
    }

    /// <summary>Parses one quoted HTTP entity tag.</summary>
    /// <param name="value">The complete entity-tag field value.</param>
    /// <returns>The parsed entity tag.</returns>
    /// <exception cref="FormatException"><paramref name="value"/> is not one valid entity tag.</exception>
    public static EntityTag Parse(string value)
    {
        return TryParse(value, out var entityTag)
            ? entityTag
            : throw new FormatException($"Invalid HTTP entity tag: '{value}'.");
    }

    /// <summary>Attempts to parse one quoted HTTP entity tag.</summary>
    /// <param name="value">The complete entity-tag field value.</param>
    /// <param name="entityTag">The parsed entity tag when successful.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is one valid entity tag.</returns>
    public static bool TryParse(string? value, [NotNullWhen(true)] out EntityTag? entityTag)
    {
        entityTag = null;

        if (string.IsNullOrWhiteSpace(value) || !EntityTagHeaderValue.TryParse(value, out var parsed) || parsed is null)
        {
            return false;
        }

        var tag = parsed.Tag.Value;
        if (string.IsNullOrEmpty(tag) || tag.Length < 2 || tag[0] != '"' || tag[^1] != '"')
        {
            return false;
        }

        entityTag = new EntityTag(parsed.ToString(), tag[1..^1], parsed.IsWeak);
        return true;
    }

    /// <summary>Attempts to decode the opaque value as Base64 bytes.</summary>
    /// <param name="value">The decoded bytes when successful; otherwise an empty array.</param>
    /// <returns><see langword="true"/> when the opaque value is valid non-empty Base64.</returns>
    public bool TryGetBytes(out byte[] value)
    {
        try
        {
            value = Convert.FromBase64String(OpaqueValue);
            return value.Length > 0;
        }
        catch (FormatException)
        {
            value = [];
            return false;
        }
    }

    /// <summary>Attempts to decode the opaque value as an unsigned 32-bit version in network byte order.</summary>
    /// <param name="value">The decoded version when successful.</param>
    /// <returns><see langword="true"/> when the opaque value contains exactly four Base64-encoded bytes.</returns>
    public bool TryGetUInt32(out uint value)
    {
        if (!TryGetBytes(out var bytes) || bytes.Length != sizeof(uint))
        {
            value = default;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        return true;
    }

    /// <summary>Returns the complete HTTP field value.</summary>
    public override string ToString() => HeaderValue;

    private static EntityTag _Create(string opaqueValue, bool isWeak)
    {
        Argument.IsNotNull(opaqueValue);

        var headerValue = isWeak ? $"W/\"{opaqueValue}\"" : $"\"{opaqueValue}\"";
        if (!TryParse(headerValue, out var entityTag) || entityTag.IsWeak != isWeak)
        {
            throw new ArgumentException(
                "The value contains characters that are invalid in an HTTP entity tag.",
                nameof(opaqueValue)
            );
        }

        return entityTag;
    }
}
