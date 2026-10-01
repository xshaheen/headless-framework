// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.SourceGenerators;

/// <summary>
/// The <c>owner.name</c> identity rule every Headless handler declaration shares, checked at build time so a malformed
/// identity never reaches a broker name, a persisted job row, or a host filter.
/// </summary>
/// <remarks>
/// The owner is the text before the first <c>.</c> and names the owning module or service; host filters such as
/// <c>billing.*</c> match on it. Segments are never empty, so <c>.a</c>, <c>a.</c>, and <c>a..b</c> are rejected. The
/// storage rules for persisted identities apply as well: at most <see cref="MaxLength"/> UTF-16 code units, no
/// surrounding whitespace, no control characters, and no unpaired surrogates.
/// </remarks>
internal static class HandlerIdentity
{
    /// <summary>The longest identity a handler may declare, in UTF-16 code units.</summary>
    public const int MaxLength = 200;

    public static bool IsValid(string? identity)
    {
        if (
            identity is not { Length: > 0 and <= MaxLength }
            || char.IsWhiteSpace(identity[0])
            || char.IsWhiteSpace(identity[identity.Length - 1])
        )
        {
            return false;
        }

        var segmentLength = 0;
        var segments = 0;
        for (var i = 0; i < identity.Length; i++)
        {
            var c = identity[i];
            if (char.IsControl(c))
            {
                return false;
            }

            if (char.IsSurrogate(c))
            {
                if (!char.IsHighSurrogate(c) || i + 1 == identity.Length || !char.IsLowSurrogate(identity[i + 1]))
                {
                    return false;
                }

                i++;
                segmentLength++;
                continue;
            }

            if (c == '.')
            {
                if (segmentLength == 0)
                {
                    return false;
                }

                segments++;
                segmentLength = 0;
                continue;
            }

            segmentLength++;
        }

        // A trailing segment is required, and at least one separator must precede it.
        return segmentLength > 0 && segments > 0;
    }
}
