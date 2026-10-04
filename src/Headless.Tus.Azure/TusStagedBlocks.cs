// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus.Models;

/// <summary>
/// Identifies the blocks staged (but not yet committed) by the most recent checksum-header append
/// as a constant-size <c>(token, firstIndex, count)</c> triple. Staged block IDs are a pure
/// function of the per-call token and consecutive indices, so the triple reconstructs the exact ID
/// list at commit time — storing the IDs themselves (37 characters per block, comma-joined) could
/// exceed Azure's 8&#160;KB total blob-metadata cap for a large single PATCH and permanently wedge
/// the upload.
/// </summary>
internal readonly record struct TusStagedBlocks(string Token, int FirstIndex, int Count)
{
    private const char _Separator = ':';

    /// <summary>Serializes the triple as the <c>tus_last_chunk_blocks</c> metadata value.</summary>
    public string ToMetadataValue()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Token}{_Separator}{FirstIndex}{_Separator}{Count}");
    }

    /// <summary>
    /// Parses a <c>tus_last_chunk_blocks</c> metadata value, returning <see langword="null"/> for
    /// missing or malformed values (corrupted metadata must degrade to "no staged chunk", the same
    /// state as a missing key, rather than throw during property reads).
    /// </summary>
    public static TusStagedBlocks? Parse(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var parts = value.Split(_Separator);

        if (
            parts.Length != 3
            || string.IsNullOrEmpty(parts[0])
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var firstIndex)
            || !int.TryParse(parts[2], CultureInfo.InvariantCulture, out var count)
            || firstIndex < 0
            || count <= 0
        )
        {
            return null;
        }

        return new TusStagedBlocks(parts[0], firstIndex, count);
    }
}
