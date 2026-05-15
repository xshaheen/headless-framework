// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal static class DateTimeUtcExtensions
{
    /// <summary>
    /// Converts a nullable <see cref="DateTime"/> to a SQL parameter value suitable for UTC timestamp columns.
    /// Returns <see cref="DBNull.Value"/> when <paramref name="value"/> is <see langword="null"/>.
    /// Non-UTC values are converted via <see cref="DateTime.ToUniversalTime"/> rather than throwing,
    /// matching the documented contract on <see cref="Messages.MediumMessage.NextRetryAt"/> that
    /// "non-UTC will be normalized" without surprising callers at runtime.
    /// </summary>
    internal static object ToUtcParameterValue(this DateTime? value)
    {
        if (!value.HasValue)
        {
            return DBNull.Value;
        }

        var v = value.Value;

        return v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime();
    }

    /// <summary>
    /// Returns <see langword="null"/> when <paramref name="value"/> is <see langword="null"/>.
    /// Non-UTC values are converted via <see cref="DateTime.ToUniversalTime"/> rather than throwing,
    /// matching the documented contract on <see cref="Messages.MediumMessage.NextRetryAt"/> that
    /// "non-UTC will be normalized" without surprising callers at runtime.
    /// </summary>
    internal static DateTime? ToUtcOrSelf(this DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var v = value.Value;

        return v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime();
    }
}
