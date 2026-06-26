// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Constants;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class DateTimeOffsetExtensions
{
    /// <summary>
    /// Converts the specified <see cref="DateTimeOffset"/> to the given <see cref="TimeZoneInfo"/>.
    /// </summary>
    /// <param name="dateTimeOffset">The date and time to convert.</param>
    /// <param name="timezone">The target time zone.</param>
    /// <returns>A <see cref="DateTimeOffset"/> representing the same moment in the specified time zone.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timezone"/> is <see langword="null"/>.</exception>
    [SystemPure]
    [JetBrainsPure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTimeOffset ToTimezone(this DateTimeOffset dateTimeOffset, TimeZoneInfo timezone)
    {
        return TimeZoneInfo.ConvertTime(dateTimeOffset, timezone);
    }

    /// <summary>Returns a copy of <paramref name="dateTime"/> with the time-of-day reset to midnight, preserving its offset.</summary>
    /// <param name="dateTime">The <see cref="DateTimeOffset"/> whose date and offset are preserved.</param>
    /// <returns>A new <see cref="DateTimeOffset"/> at the start of the same day with the same offset.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset ClearTime(this DateTimeOffset dateTime)
    {
        // Subtracting the time-of-day lands on midnight without re-validating calendar parts; subtraction preserves Offset.
        return dateTime - dateTime.TimeOfDay;
    }

    /// <summary>Returns the start of the day (midnight) that contains <paramref name="dateTimeOffset"/> as seen at the given UTC <paramref name="offset"/>.</summary>
    /// <param name="dateTimeOffset">The instant to evaluate.</param>
    /// <param name="offset">The UTC offset whose local calendar day defines the day boundary.</param>
    /// <returns>A <see cref="DateTimeOffset"/> at midnight of the day, using <paramref name="offset"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset GetStartOfDay(this DateTimeOffset dateTimeOffset, TimeSpan offset)
    {
        return dateTimeOffset.ToOffset(offset).ClearTime();
    }

    /// <summary>Returns the last representable instant of the day (one millisecond before the next midnight) that contains <paramref name="dateTimeOffset"/> at the given UTC <paramref name="offset"/>.</summary>
    /// <param name="dateTimeOffset">The instant to evaluate.</param>
    /// <param name="offset">The UTC offset whose local calendar day defines the day boundary.</param>
    /// <returns>A <see cref="DateTimeOffset"/> at <c>23:59:59.999</c> of the day, using <paramref name="offset"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset GetEndOfDay(this DateTimeOffset dateTimeOffset, TimeSpan offset)
    {
        return dateTimeOffset.GetStartOfDay(offset).AddDays(1).AddMilliseconds(-1);
    }

    /// <summary>Returns the first instant (midnight on day 1) of the month that contains <paramref name="dateTime"/> at the given UTC <paramref name="offset"/>.</summary>
    /// <param name="dateTime">The instant to evaluate.</param>
    /// <param name="offset">The UTC offset whose local calendar defines the month boundary.</param>
    /// <returns>A <see cref="DateTimeOffset"/> at midnight on the first day of the month, using <paramref name="offset"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset GetStartOfMonth(this DateTimeOffset dateTime, TimeSpan offset)
    {
        var d = dateTime.ToOffset(offset);

        return new DateTimeOffset(d.Year, d.Month, 1, 0, 0, 0, d.Offset);
    }

    /// <summary>Returns the last representable instant of the month that contains <paramref name="dateTime"/> at the given UTC <paramref name="offset"/>.</summary>
    /// <param name="dateTime">The instant to evaluate.</param>
    /// <param name="offset">The UTC offset whose local calendar defines the month boundary.</param>
    /// <returns>A <see cref="DateTimeOffset"/> at <c>23:59:59.999</c> on the last day of the month, using <paramref name="offset"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset GetEndOfMonth(this DateTimeOffset dateTime, TimeSpan offset)
    {
        var d = dateTime.ToOffset(offset);
        var days = DateTime.DaysInMonth(d.Year, d.Month);
        var endOfMonthDay = new DateTimeOffset(d.Year, d.Month, days, 0, 0, 0, d.Offset);

        return endOfMonthDay.GetEndOfDay(offset);
    }

    /// <summary>Returns the first instant (midnight on January 1) of the year that contains <paramref name="dateTime"/> at the given UTC <paramref name="offset"/>.</summary>
    /// <param name="dateTime">The instant to evaluate.</param>
    /// <param name="offset">The UTC offset whose local calendar defines the year boundary.</param>
    /// <returns>A <see cref="DateTimeOffset"/> at midnight on January 1 of the year, using <paramref name="offset"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset GetStartOfYear(this DateTimeOffset dateTime, TimeSpan offset)
    {
        var d = dateTime.ToOffset(offset);

        return new DateTimeOffset(d.Year, 1, 1, 0, 0, 0, d.Offset);
    }

    /// <summary>Returns the last representable instant of the year that contains <paramref name="dateTime"/> at the given UTC <paramref name="offset"/>.</summary>
    /// <param name="dateTime">The instant to evaluate.</param>
    /// <param name="offset">The UTC offset whose local calendar defines the year boundary.</param>
    /// <returns>A <see cref="DateTimeOffset"/> at <c>23:59:59.999</c> on December 31 of the year, using <paramref name="offset"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset GetEndOfYear(this DateTimeOffset dateTime, TimeSpan offset)
    {
        var d = dateTime.ToOffset(offset);
        var startOfTheDay = new DateTimeOffset(d.Year, 12, DateTime.DaysInMonth(d.Year, 12), 0, 0, 0, d.Offset);

        return startOfTheDay.GetEndOfDay(offset);
    }

    /// <summary>
    /// Returns a new <see cref="DateTimeOffset"/> object that removes time information beyond millisecond precision from the provided instance.
    /// </summary>
    /// <param name="date">The <see cref="DateTimeOffset"/> object that would be used as a source for the non-truncated time parts.</param>
    /// <returns>
    /// An object that is equivalent to <paramref name="date" /> up to millisecond precision, and empty beyond milliseconds.
    /// </returns>
    /// <remarks>
    /// Sub-millisecond ticks are floored (truncated toward the millisecond boundary), so the result is never later than the
    /// original <paramref name="date" />.
    /// </remarks>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset TruncateToMilliseconds(this DateTimeOffset date)
    {
        // Floor the ticks to the millisecond boundary; AddTicks preserves Offset and avoids re-validating calendar parts.
        return date.AddTicks(-(date.Ticks % TimeSpan.TicksPerMillisecond));
    }

    /// <summary>
    /// Returns a new <see cref="DateTimeOffset"/> object that removes time information beyond second precision from the provided instance.
    /// </summary>
    /// <param name="date">The <see cref="DateTimeOffset"/> object that would be used as a source for the non-truncated time parts.</param>
    /// <returns>
    /// An object that is equivalent to <paramref name="date" /> up to second precision, and empty beyond seconds.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset TruncateToSeconds(this DateTimeOffset date)
    {
        // Floor the ticks to the second boundary; AddTicks preserves Offset and avoids re-validating calendar parts.
        return date.AddTicks(-(date.Ticks % TimeSpan.TicksPerSecond));
    }

    /// <summary>
    /// Returns a new <see cref="DateTimeOffset"/> object that removes time information beyond minute precision from the provided instance.
    /// </summary>
    /// <param name="date">The <see cref="DateTimeOffset"/> object that would be used as a source for the non-truncated time parts.</param>
    /// <returns>
    /// An object that is equivalent to <paramref name="date" /> up to minute precision, and empty beyond minutes.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset TruncateToMinutes(this DateTimeOffset date)
    {
        // Floor the ticks to the minute boundary; AddTicks preserves Offset and avoids re-validating calendar parts.
        return date.AddTicks(-(date.Ticks % TimeSpan.TicksPerMinute));
    }

    /// <summary>
    /// Returns a new <see cref="DateTimeOffset"/> object that removes time information beyond hour precision from the provided instance.
    /// </summary>
    /// <param name="date">The <see cref="DateTimeOffset"/> object that would be used as a source for the non-truncated time parts.</param>
    /// <returns>
    /// An object that is equivalent to <paramref name="date" /> up to hour precision, and empty beyond hours.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset TruncateToHours(this DateTimeOffset date)
    {
        // Floor the ticks to the hour boundary; AddTicks preserves Offset and avoids re-validating calendar parts.
        return date.AddTicks(-(date.Ticks % TimeSpan.TicksPerHour));
    }

    /// <summary>
    /// Safely adds a specified <see cref="TimeSpan"/> to the given <see cref="DateTimeOffset"/>.
    /// </summary>
    /// <param name="date">The <see cref="DateTimeOffset"/> to which the <paramref name="value"/> will be added.</param>
    /// <param name="value">The <see cref="TimeSpan"/> to add.</param>
    /// <returns>
    /// A new <see cref="DateTimeOffset"/> that is the sum of the original <paramref name="date"/> and the <paramref name="value"/>.
    /// If the result is less than <see cref="DateTimeOffset.MinValue"/>, <see cref="DateTimeOffset.MinValue"/> is returned.
    /// If the result is greater than <see cref="DateTimeOffset.MaxValue"/>, <see cref="DateTimeOffset.MaxValue"/> is returned.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset SafeAdd(this DateTimeOffset date, TimeSpan value)
    {
        // Compare against the bounds without computing date.Ticks + value.Ticks, which can overflow long
        // (TimeSpan.Ticks spans the full long range) before the clamp would ever run.
        if (value.Ticks > 0 && date.Ticks > DateTimeOffset.MaxValue.Ticks - value.Ticks)
        {
            return DateTimeOffset.MaxValue;
        }

        if (value.Ticks < 0 && date.Ticks < DateTimeOffset.MinValue.Ticks - value.Ticks)
        {
            return DateTimeOffset.MinValue;
        }

        return date.Add(value);
    }

    /// <summary>
    /// Floors the given <see cref="DateTimeOffset"/> to the nearest interval of the specified <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="date">The <see cref="DateTimeOffset"/> to floor.</param>
    /// <param name="interval">The <see cref="TimeSpan"/> interval to floor to.</param>
    /// <returns>A new <see cref="DateTimeOffset"/> floored to the nearest interval of the specified <paramref name="interval"/>.</returns>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="interval"/> is <see cref="TimeSpan.Zero"/>.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset Floor(this DateTimeOffset date, TimeSpan interval)
    {
        return date.AddTicks(-(date.Ticks % interval.Ticks));
    }

    /// <summary>
    /// Ceils the given <see cref="DateTimeOffset"/> to the nearest interval of the specified <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="date">The <see cref="DateTimeOffset"/> to ceil.</param>
    /// <param name="interval">The <see cref="TimeSpan"/> interval to ceil to.</param>
    /// <returns>A new <see cref="DateTimeOffset"/> ceiled to the nearest interval of the specified <paramref name="interval"/>.</returns>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="interval"/> is <see cref="TimeSpan.Zero"/>.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset Ceiling(this DateTimeOffset date, TimeSpan interval)
    {
        return date.AddTicks(interval.Ticks - (date.Ticks % interval.Ticks));
    }

    /// <summary>Extracts the local (offset-relative) date portion of <paramref name="date"/> as a <see cref="DateOnly"/>.</summary>
    /// <param name="date">The <see cref="DateTimeOffset"/> to convert.</param>
    /// <returns>A <see cref="DateOnly"/> for the calendar date as observed at <paramref name="date"/>'s own offset.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateOnly ToDateOnly(this DateTimeOffset date)
    {
        return DateOnly.FromDateTime(date.DateTime);
    }

    /// <summary>Extracts the UTC date portion of <paramref name="date"/> as a <see cref="DateOnly"/>.</summary>
    /// <param name="date">The <see cref="DateTimeOffset"/> to convert.</param>
    /// <returns>A <see cref="DateOnly"/> for the UTC calendar date of <paramref name="date"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateOnly ToUtcDateOnly(this DateTimeOffset date)
    {
        return DateOnly.FromDateTime(date.UtcDateTime);
    }

    /// <summary>Extracts the local (offset-relative) time-of-day of <paramref name="date"/> as a <see cref="TimeOnly"/>.</summary>
    /// <param name="date">The <see cref="DateTimeOffset"/> to convert.</param>
    /// <returns>A <see cref="TimeOnly"/> for the time-of-day as observed at <paramref name="date"/>'s own offset.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static TimeOnly ToTimeOnly(this DateTimeOffset date)
    {
        return TimeOnly.FromDateTime(date.DateTime);
    }

    /// <summary>Extracts the UTC time-of-day of <paramref name="date"/> as a <see cref="TimeOnly"/>.</summary>
    /// <param name="date">The <see cref="DateTimeOffset"/> to convert.</param>
    /// <returns>A <see cref="TimeOnly"/> for the UTC time-of-day of <paramref name="date"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static TimeOnly ToUtcTimeOnly(this DateTimeOffset date)
    {
        return TimeOnly.FromDateTime(date.UtcDateTime);
    }

    /// <summary>Converts <paramref name="dateTimeOffset"/> to the Egypt time zone (see <see cref="TimezoneConstants.EgyptTimeZone"/>).</summary>
    /// <param name="dateTimeOffset">The instant to convert.</param>
    /// <returns>A <see cref="DateTimeOffset"/> representing the same moment expressed in the Egypt time zone.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset ToEgyptTimeZone(this DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.ToTimezone(TimezoneConstants.EgyptTimeZone);
    }

    /// <summary>Converts <paramref name="dateTimeOffset"/> to the Saudi Arabia time zone (see <see cref="TimezoneConstants.SaudiArabiaTimeZone"/>).</summary>
    /// <param name="dateTimeOffset">The instant to convert.</param>
    /// <returns>A <see cref="DateTimeOffset"/> representing the same moment expressed in the Saudi Arabia time zone.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset ToSaudiArabiaTimeZone(this DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.ToTimezone(TimezoneConstants.SaudiArabiaTimeZone);
    }
}
