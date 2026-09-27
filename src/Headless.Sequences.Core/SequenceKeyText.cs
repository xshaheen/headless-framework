// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

/// <summary>Text rules every key part (tenant id, name, partition) follows on every provider.</summary>
internal static class SequenceKeyText
{
    /// <summary>
    /// Whether <paramref name="value" /> starts or ends with whitespace. SQL Server pads <c>nvarchar</c> values
    /// with trailing spaces before comparing them, under every collation and in primary-key uniqueness, so
    /// <c>"acme"</c> and <c>"acme "</c> would share one counter there while PostgreSQL keeps them apart. Refusing
    /// surrounding whitespace keeps key parts ordinal on both providers.
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
                $"A sequence {what} must not start or end with whitespace: some providers ignore trailing spaces "
                    + "when comparing keys, which would merge two counters.",
                paramName
            );
        }
    }
}
