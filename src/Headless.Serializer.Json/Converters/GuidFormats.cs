// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer.Converters;

/// <summary>
/// Shared parsing helper for the standard <see cref="Guid"/> string formats
/// (<c>N</c>, <c>D</c>, <c>B</c>, <c>P</c>, <c>X</c>) used by the JSON converters.
/// </summary>
internal static class GuidFormats
{
    /// <summary>
    /// Attempts to parse <paramref name="value"/> as a <see cref="Guid"/> using each of the standard
    /// exact formats (<c>N</c>, <c>D</c>, <c>B</c>, <c>P</c>, <c>X</c>) in order.
    /// </summary>
    public static bool TryParseAny(string? value, out Guid result)
    {
        foreach (var format in (ReadOnlySpan<string>)["N", "D", "B", "P", "X"])
        {
            if (Guid.TryParseExact(value, format, out result))
            {
                return true;
            }
        }

        result = default;

        return false;
    }
}
