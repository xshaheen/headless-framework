// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>Text rules every lease-key part (tenant id, kind, resource) follows on every provider.</summary>
internal static class LeaseKeyText
{
    /// <summary>
    /// Whether <paramref name="value" /> starts or ends with whitespace. SQL Server pads <c>nvarchar</c> values with
    /// trailing spaces before comparing them, under every collation and in primary-key uniqueness, so <c>"a"</c> and
    /// <c>"a "</c> would share one lease there while PostgreSQL keeps them apart. Refusing surrounding whitespace
    /// keeps key parts ordinal on both providers.
    /// </summary>
    public static bool HasSurroundingWhitespace(string value)
    {
        return value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]));
    }

    /// <summary>Throws when <paramref name="value" /> starts or ends with whitespace.</summary>
    /// <exception cref="ArgumentException"><paramref name="value" /> starts or ends with whitespace.</exception>
    public static void EnsureNoSurroundingWhitespace(string value, string what, string paramName)
    {
        if (HasSurroundingWhitespace(value))
        {
            throw new ArgumentException(
                $"A lease {what} must not start or end with whitespace: some providers ignore trailing spaces when "
                    + "comparing keys, which would merge two leases.",
                paramName
            );
        }
    }
}
