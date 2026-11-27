// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using Headless.Checks;

namespace Headless.Sequences;

/// <summary>
/// The text forms of a counter key the module serves: the object id, the list cursor, and the partitions cursor.
/// </summary>
/// <remarks>
/// <para>
/// An id is the tenant id, name, and partition, each escaped with <see cref="Uri.EscapeDataString(string)"/> and
/// joined by <c>/</c>: <c>acme/invoice/2026</c>, or <c>/ticket/</c> for a host counter with no partition. Escaping
/// keeps a <c>/</c> or <c>%</c> inside a part from reading as a separator, so every key has exactly one id and reads
/// back exact. Only the canonical form parses, so an id never names a key through two spellings, and a partition parses
/// only when a counter can be stored under it.
/// </para>
/// <para>
/// The id travels as a query value, never a path segment: a router decodes an escaped <c>/</c> in a path into a
/// separator. A cursor is a versioned prefix and the base64url of its text, so it survives any query encoding and
/// reads as opaque to the caller.
/// </para>
/// </remarks>
internal static class SequenceCounterId
{
    private const string _ListCursorPrefix = "c1.";
    private const string _PartitionCursorPrefix = "p1.";

    public static string Format(SequenceKey key) =>
        string.Join(
            '/',
            Uri.EscapeDataString(key.TenantId),
            Uri.EscapeDataString(key.Name),
            Uri.EscapeDataString(key.Partition)
        );

    /// <summary>Reads an id this module served; a name is required, the tenant and partition may be empty.</summary>
    public static bool TryParse(string? id, out SequenceKey key)
    {
        key = default;

        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        var parts = id.Split('/');

        if (parts.Length != 3 || parts[1].Length == 0)
        {
            return false;
        }

        var parsed = new SequenceKey(
            Uri.UnescapeDataString(parts[0]),
            Uri.UnescapeDataString(parts[1]),
            Uri.UnescapeDataString(parts[2])
        );

        // UnescapeDataString leaves a malformed escape as typed, so an id that does not format back is not one the
        // module served.
        if (!string.Equals(Format(parsed), id, StringComparison.Ordinal) || !_IsStorablePartition(parsed.Partition))
        {
            return false;
        }

        key = parsed;

        return true;
    }

    /// <summary>
    /// Whether a partition is one a counter can be stored under: empty for none, else within
    /// <see cref="SequenceFieldLimits.PartitionMaxLength"/> and portable key text. The partitions read takes no
    /// partition, so nothing downstream checks the one an id carries, and an id holding any other text names no counter.
    /// </summary>
    private static bool _IsStorablePartition(string partition)
    {
        if (partition.Length == 0)
        {
            return true;
        }

        if (partition.Length > SequenceFieldLimits.PartitionMaxLength)
        {
            return false;
        }

        try
        {
            Argument.IsPortableKey(partition);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return true;
    }

    public static string FormatListCursor(SequenceKey key) => _ListCursorPrefix + _Encode(Format(key));

    public static bool TryParseListCursor(string cursor, out SequenceKey key)
    {
        key = default;

        return _TryDecode(cursor, _ListCursorPrefix, out var id) && TryParse(id, out key);
    }

    public static string FormatPartitionCursor(string partition) => _PartitionCursorPrefix + _Encode(partition);

    public static bool TryParsePartitionCursor(string cursor, [NotNullWhen(true)] out string? partition)
    {
        return _TryDecode(cursor, _PartitionCursorPrefix, out partition) && partition.Length > 0;
    }

    private static string _Encode(string text) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));

    private static bool _TryDecode(string cursor, string prefix, [NotNullWhen(true)] out string? text)
    {
        text = null;

        if (!cursor.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] bytes;

        try
        {
            bytes = Base64Url.DecodeFromChars(cursor.AsSpan(prefix.Length));
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return true;
    }
}
