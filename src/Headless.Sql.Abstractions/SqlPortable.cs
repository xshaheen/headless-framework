// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sql;

/// <summary>
/// The time resolution every supported engine stores and returns unchanged. A feature applies it at its shared call
/// validation, before any provider sees a value, so no provider rounds an instant differently from another. Key text
/// has the same concern and is checked by <c>Argument.IsPortableKey</c>.
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
}
