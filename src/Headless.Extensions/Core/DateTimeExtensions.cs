// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class DateTimeExtensions
{
    /// <summary>
    /// Converts the specified <see cref="DateTime"/> to a <see cref="DateTimeOffset"/> in the given <see cref="TimeZoneInfo"/>,
    /// considering the date and time as unspecified and applying the correct offset (including DST if applicable).
    /// </summary>
    /// <param name="dateTime">The <see cref="DateTime"/> to convert.</param>
    /// <param name="timezone">The target <see cref="TimeZoneInfo"/>.</param>
    /// <returns>A <see cref="DateTimeOffset"/> representing the same date and time in the specified timezone.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timezone"/> is <see langword="null"/>.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset AsTimezone(this DateTime dateTime, TimeZoneInfo timezone)
    {
        Argument.IsNotNull(timezone);
        var unspecifiedDateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
        var offset = timezone.GetUtcOffset(unspecifiedDateTime); // Use GetUtcOffset to account for DST if applicable

        return new DateTimeOffset(unspecifiedDateTime, offset);
    }

    /// <summary>Returns a copy of <paramref name="dateTime"/> with the time-of-day reset to midnight (00:00:00).</summary>
    /// <param name="dateTime">The <see cref="DateTime"/> whose date portion is preserved.</param>
    /// <returns>A new <see cref="DateTime"/> at the start of the same day, retaining the original <see cref="DateTime.Kind"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTime ClearTime(this DateTime dateTime)
    {
        // Date keeps the original Kind (Utc/Local/Unspecified); a new DateTime(y, m, d) would reset it to Unspecified.
        return dateTime.Date;
    }

    /// <summary>
    /// Returns a new <see cref="DateTime"/> object that removes time information beyond millisecond precision from the provided instance.
    /// </summary>
    /// <param name="date">The <see cref="DateTime"/> object that would be used as a source for the non-truncated time parts.</param>
    /// <returns>
    /// An object that is equivalent to <paramref name="date" /> up to millisecond precision, and empty beyond milliseconds.
    /// </returns>
    /// <remarks>
    /// Note that the end result might be in the future relative to the original <paramref name="date" />. <see cref="DateTimeOffset.Millisecond" /> represents
    /// a rounded value for ticks—so 10 milliseconds might internally be 9.6 milliseconds. However, this information is lost after this method, and
    /// the value would be replaced with 10 milliseconds.
    /// </remarks>
    [SystemPure]
    [JetBrainsPure]
    public static DateTime TruncateToMilliseconds(this DateTime date)
    {
        // Floor the ticks to the millisecond boundary; AddTicks preserves Kind and avoids re-validating calendar parts.
        return date.AddTicks(-(date.Ticks % TimeSpan.TicksPerMillisecond));
    }

    /// <summary>
    /// Returns a new <see cref="DateTime"/> object that removes time information beyond second precision from the provided instance.
    /// </summary>
    /// <param name="date">The <see cref="DateTime"/> object that would be used as a source for the non-truncated time parts.</param>
    /// <returns>
    /// An object that is equivalent to <paramref name="date" /> up to second precision, and empty beyond seconds.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTime TruncateToSeconds(this DateTime date)
    {
        // Floor the ticks to the second boundary; AddTicks preserves Kind and avoids re-validating calendar parts.
        return date.AddTicks(-(date.Ticks % TimeSpan.TicksPerSecond));
    }

    /// <summary>
    /// Returns a new <see cref="DateTime"/> object that removes time information beyond minute precision from the provided instance.
    /// </summary>
    /// <param name="date">The <see cref="DateTime"/> object that would be used as a source for the non-truncated time parts.</param>
    /// <returns>
    /// An object that is equivalent to <paramref name="date" /> up to minute precision, and empty beyond minutes.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTime TruncateToMinutes(this DateTime date)
    {
        // Floor the ticks to the minute boundary; AddTicks preserves Kind and avoids re-validating calendar parts.
        return date.AddTicks(-(date.Ticks % TimeSpan.TicksPerMinute));
    }

    /// <summary>
    /// Returns a new <see cref="DateTime"/> object that removes time information beyond hour precision from the provided instance.
    /// </summary>
    /// <param name="date">The <see cref="DateTime"/> object that would be used as a source for the non-truncated time parts.</param>
    /// <returns>
    /// An object that is equivalent to <paramref name="date" /> up to hour precision, and empty beyond hours.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTime TruncateToHours(this DateTime date)
    {
        // Floor the ticks to the hour boundary; AddTicks preserves Kind and avoids re-validating calendar parts.
        return date.AddTicks(-(date.Ticks % TimeSpan.TicksPerHour));
    }

    /// <summary>Converts the specified <see cref="DateTime"/> to Unix time in seconds. </summary>
    /// <param name="date">The <see cref="DateTime"/> to convert.</param>
    /// <returns>The number of seconds that have elapsed since 1970-01-01T00:00:00Z.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static long ToUnixTimeSeconds(this DateTime date)
    {
        return new DateTimeOffset(date.ToUniversalTime()).ToUnixTimeSeconds();
    }

    /// <summary>Converts the specified <see cref="DateTime"/> to Unix time in milliseconds.</summary>
    /// <param name="date">The <see cref="DateTime"/> to convert.</param>
    /// <returns>The number of milliseconds that have elapsed since 1970-01-01T00:00:00Z.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static long ToUnixTimeMilliseconds(this DateTime date)
    {
        return new DateTimeOffset(date.ToUniversalTime()).ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Safely adds a specified <see cref="TimeSpan"/> to the given <see cref="DateTime"/>.
    /// </summary>
    /// <param name="date">The <see cref="DateTime"/> to which the <paramref name="value"/> will be added.</param>
    /// <param name="value">The <see cref="TimeSpan"/> to add.</param>
    /// <returns>
    /// A new <see cref="DateTime"/> that is the sum of the original <paramref name="date"/> and the <paramref name="value"/>.
    /// If the result is less than <see cref="DateTime.MinValue"/>, <see cref="DateTime.MinValue"/> is returned.
    /// If the result is greater than <see cref="DateTime.MaxValue"/>, <see cref="DateTime.MaxValue"/> is returned.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTime SafeAdd(this DateTime date, TimeSpan value)
    {
        // Compare against the bounds without computing date.Ticks + value.Ticks, which can overflow long
        // (TimeSpan.Ticks spans the full long range) before the clamp would ever run.
        if (value.Ticks > 0 && date.Ticks > DateTime.MaxValue.Ticks - value.Ticks)
        {
            return DateTime.MaxValue;
        }

        if (value.Ticks < 0 && date.Ticks < DateTime.MinValue.Ticks - value.Ticks)
        {
            return DateTime.MinValue;
        }

        return date.Add(value);
    }

    /// <summary>
    /// Floors the given <see cref="DateTime"/> to the nearest interval of the specified <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="date">The <see cref="DateTime"/> to floor.</param>
    /// <param name="interval">The <see cref="TimeSpan"/> interval to floor to.</param>
    /// <returns>A new <see cref="DateTime"/> floored to the nearest interval of the specified <paramref name="interval"/>.</returns>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="interval"/> is <see cref="TimeSpan.Zero"/>.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static DateTime Floor(this DateTime date, TimeSpan interval)
    {
        return date.AddTicks(-(date.Ticks % interval.Ticks));
    }

    /// <summary>
    /// Ceils the given <see cref="DateTime"/> to the nearest interval of the specified <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="date">The <see cref="DateTime"/> to ceil.</param>
    /// <param name="interval">The <see cref="TimeSpan"/> interval to ceil to.</param>
    /// <returns>A new <see cref="DateTime"/> ceiled to the nearest interval of the specified <paramref name="interval"/>.</returns>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="interval"/> is <see cref="TimeSpan.Zero"/>.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static DateTime Ceiling(this DateTime date, TimeSpan interval)
    {
        return date.AddTicks(interval.Ticks - (date.Ticks % interval.Ticks));
    }

    /// <summary>Extracts the date portion of <paramref name="date"/> as a <see cref="DateOnly"/>, ignoring the time-of-day.</summary>
    /// <param name="date">The <see cref="DateTime"/> to convert.</param>
    /// <returns>A <see cref="DateOnly"/> for the same calendar date, without any time-zone conversion.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateOnly ToDateOnly(this DateTime date)
    {
        return DateOnly.FromDateTime(date);
    }

    /// <summary>Converts <paramref name="date"/> to UTC and extracts the resulting date portion as a <see cref="DateOnly"/>.</summary>
    /// <param name="date">The <see cref="DateTime"/> to convert to UTC before extracting the date.</param>
    /// <returns>A <see cref="DateOnly"/> for the UTC calendar date corresponding to <paramref name="date"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateOnly ToUtcDateOnly(this DateTime date)
    {
        return DateOnly.FromDateTime(date.ToUniversalTime());
    }

    /// <summary>Extracts the time-of-day portion of <paramref name="date"/> as a <see cref="TimeOnly"/>, ignoring the date.</summary>
    /// <param name="date">The <see cref="DateTime"/> to convert.</param>
    /// <returns>A <see cref="TimeOnly"/> for the same time-of-day, without any time-zone conversion.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static TimeOnly ToTimeOnly(this DateTime date)
    {
        return TimeOnly.FromDateTime(date);
    }

    /// <summary>Converts <paramref name="date"/> to UTC and extracts the resulting time-of-day as a <see cref="TimeOnly"/>.</summary>
    /// <param name="date">The <see cref="DateTime"/> to convert to UTC before extracting the time-of-day.</param>
    /// <returns>A <see cref="TimeOnly"/> for the UTC time-of-day corresponding to <paramref name="date"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static TimeOnly ToUtcTimeOnly(this DateTime date)
    {
        return TimeOnly.FromDateTime(date.ToUniversalTime());
    }

    /// <summary>
    /// Normalizes <paramref name="date"/> to <see cref="DateTimeKind.Utc"/> so an instant can be persisted or
    /// compared without depending on the ambient machine timezone.
    /// </summary>
    /// <param name="date">The value to normalize.</param>
    /// <returns>
    /// A <see cref="DateTime"/> with <see cref="DateTimeKind.Utc"/>. <see cref="DateTimeKind.Local"/> values are
    /// converted; <see cref="DateTimeKind.Unspecified"/> values are re-stamped in place.
    /// </returns>
    /// <remarks>
    /// <see cref="DateTimeKind.Unspecified"/> is assumed to ALREADY be UTC and is stamped without conversion — it is
    /// the kind relational providers hand back on read. Interpreting it as local would silently shift every value
    /// read from the database by the host's UTC offset.
    /// </remarks>
    [SystemPure]
    [JetBrainsPure]
    public static DateTime NormalizeToUtc(this DateTime date)
    {
        return date.Kind switch
        {
            DateTimeKind.Utc => date,
            DateTimeKind.Local => date.ToUniversalTime(),
            _ => DateTime.SpecifyKind(date, DateTimeKind.Utc),
        };
    }
}
