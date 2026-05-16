// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal static class DateTimeUtcExtensions
{
    /// <summary>
    /// Converts a nullable <see cref="DateTime"/> to a SQL parameter value suitable for UTC timestamp columns.
    /// Returns <see cref="DBNull.Value"/> when <paramref name="value"/> is <see langword="null"/>.
    /// Non-UTC values are normalized rather than throwing, matching the documented contract on
    /// <see cref="Messages.MediumMessage.NextRetryAt"/>:
    /// <list type="bullet">
    ///   <item><see cref="DateTimeKind.Utc"/> — returned as-is.</item>
    ///   <item><see cref="DateTimeKind.Local"/> — converted via <see cref="DateTime.ToUniversalTime"/>.</item>
    ///   <item><see cref="DateTimeKind.Unspecified"/> — assumed already-UTC and tagged via
    ///     <see cref="DateTime.SpecifyKind"/>. <see cref="DateTime.ToUniversalTime"/> would
    ///     silently shift these by the local-clock offset, which is wrong for a value the
    ///     contract documents as UTC. SpecifyKind preserves the wall-clock value.</item>
    /// </list>
    /// </summary>
    internal static object ToUtcParameterValue(this DateTime? value)
    {
        if (!value.HasValue)
        {
            return DBNull.Value;
        }

        return _NormalizeToUtc(value.Value);
    }

    /// <summary>
    /// Returns <see langword="null"/> when <paramref name="value"/> is <see langword="null"/>.
    /// Non-UTC values are normalized rather than throwing — see
    /// <see cref="ToUtcParameterValue"/> for the per-<see cref="DateTimeKind"/> rules.
    /// </summary>
    internal static DateTime? ToUtcOrSelf(this DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return _NormalizeToUtc(value.Value);
    }

    private static DateTime _NormalizeToUtc(DateTime v) =>
        v.Kind switch
        {
            DateTimeKind.Utc => v,
            DateTimeKind.Local => v.ToUniversalTime(),
            _ => DateTime.SpecifyKind(v, DateTimeKind.Utc),
        };
}
