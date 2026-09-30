// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sql;

/// <summary>
/// What every supported engine stores, compares, and returns unchanged. A feature enforces it at its shared call
/// validation, before any provider sees a value, so no provider keeps what another one merges, rejects, rewrites, or
/// rounds differently.
/// </summary>
[PublicAPI]
public static class SqlPortable
{
    /// <summary>
    /// The finest time resolution every engine keeps: PostgreSQL stores microseconds, SQL Server and .NET 100-nanosecond
    /// ticks.
    /// </summary>
    public static readonly TimeSpan TimestampPrecision = TimeSpan.FromMicroseconds(1);

    /// <summary>
    /// Truncates <paramref name="duration" /> to <see cref="TimestampPrecision" />, so an instant computed from it is
    /// the same on every engine. PostgreSQL truncates a finer interval the same way, silently.
    /// </summary>
    public static TimeSpan Truncate(TimeSpan duration)
    {
        return TimeSpan.FromTicks(duration.Ticks - (duration.Ticks % TimestampPrecision.Ticks));
    }

    /// <summary>
    /// Returns why no engine can hold <paramref name="value" /> as a key unchanged, or <see langword="null" /> when every
    /// one can.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>
    /// Surrounding whitespace: SQL Server pads the shorter string with spaces before comparing, under every collation
    /// and in unique-index uniqueness, so <c>"a"</c> and <c>"a "</c> are one key there and two on PostgreSQL.
    /// </item>
    /// <item>A NUL character: PostgreSQL text cannot hold U+0000 and fails the statement (22021).</item>
    /// <item>
    /// An unpaired UTF-16 surrogate: it is not valid Unicode, so Npgsql's UTF-8 encoder refuses it before sending, and
    /// SQL Server stores it but returns it rewritten as U+FFFD, a different key from the one stored.
    /// </item>
    /// </list>
    /// Case, accents, precomposed versus decomposed forms, surrogate pairs, and other control characters round-trip and
    /// compare ordinally on every engine with a binary or <c>"C"</c> key collation, so they stay distinct keys.
    /// </remarks>
    public static string? FindUnportableKeyText(string value)
    {
        if (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
        {
            return "starts or ends with whitespace, which SQL Server ignores when comparing keys";
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c == '\0')
            {
                return "contains a NUL character, which PostgreSQL cannot store";
            }

            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;

                continue;
            }

            if (char.IsSurrogate(c))
            {
                return "contains an unpaired UTF-16 surrogate, which is not valid Unicode and does not round-trip";
            }
        }

        return null;
    }
}
