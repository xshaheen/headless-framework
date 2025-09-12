// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Framework.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
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
    [SystemPure]
    [JetBrainsPure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTimeOffset ToTimezone(this DateTimeOffset dateTimeOffset, TimeZoneInfo timezone)
    {
        return TimeZoneInfo.ConvertTime(dateTimeOffset, timezone);
    }

    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset ClearTime(this DateTimeOffset dateTime)
    {
        return new(dateTime.Year, dateTime.Month, dateTime.Day, 0, 0, 0, dateTime.Offset);
    }

    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset GetStartOfDay(this DateTimeOffset dateTimeOffset, TimeSpan offset)
    {
        return dateTimeOffset.ToOffset(offset).ClearTime();
    }

    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset GetEndOfDay(this DateTimeOffset dateTimeOffset, TimeSpan offset)
    {
        return dateTimeOffset.GetStartOfDay(offset).AddDays(1).AddMilliseconds(-1);
    }

    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset GetStartOfMonth(this DateTimeOffset dateTime, TimeSpan offset)
    {
        var d = dateTime.ToOffset(offset);

        return new DateTimeOffset(d.Year, d.Month, 1, 0, 0, 0, d.Offset);
    }

    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset GetEndOfMonth(this DateTimeOffset dateTime, TimeSpan offset)
    {
        var d = dateTime.ToOffset(offset);
        var days = DateTime.DaysInMonth(dateTime.Year, dateTime.Month);
        var endOfMonthDay = new DateTimeOffset(d.Year, d.Month, days, 0, 0, 0, d.Offset);

        return endOfMonthDay.GetEndOfDay(offset);
    }

    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset GetStartOfYear(this DateTimeOffset dateTime, TimeSpan offset)
    {
        var d = dateTime.ToOffset(offset);

        return new DateTimeOffset(d.Year, 1, 1, 0, 0, 0, d.Offset);
    }

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
    /// Note that the end result might be in the future relative to the original <paramref name="date" />. <see cref="DateTimeOffset.Millisecond" /> represents
    /// a rounded value for ticks—so 10 milliseconds might internally be 9.6 milliseconds. However, this information is lost after this method, and
    /// the value would be replaced with 10 milliseconds.
    /// </remarks>
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset TruncateToMilliseconds(this DateTimeOffset date)
    {
        return new(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, date.Millisecond, date.Offset);
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
        return new(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, 0, date.Offset);
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
        return new(date.Year, date.Month, date.Day, date.Hour, date.Minute, 0, 0, date.Offset);
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
        return new(date.Year, date.Month, date.Day, date.Hour, 0, 0, 0, date.Offset);
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
        if (date.Ticks + value.Ticks < DateTimeOffset.MinValue.Ticks)
        {
            return DateTimeOffset.MinValue;
        }

        if (date.Ticks + value.Ticks > DateTimeOffset.MaxValue.Ticks)
        {
            return DateTimeOffset.MaxValue;
        }

        return date.Add(value);
    }

    /// <summary>
    /// Floors the given <see cref="DateTimeOffset"/> to the nearest interval of the specified <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="date">The <see cref="DateTimeOffset"/> to floor.</param>
    /// <param name="interval">The <see cref="TimeSpan"/> interval to floor to.</param>
    /// <returns>A new <see cref="DateTimeOffset"/> floored to the nearest interval of the specified <paramref name="interval"/>.</returns>
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
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset Ceiling(this DateTimeOffset date, TimeSpan interval)
    {
        return date.AddTicks(interval.Ticks - (date.Ticks % interval.Ticks));
    }

    [SystemPure]
    [JetBrainsPure]
    public static DateOnly ToDateOnly(this DateTimeOffset date)
    {
        return DateOnly.FromDateTime(date.DateTime);
    }

    [SystemPure]
    [JetBrainsPure]
    public static DateOnly ToUtcDateOnly(this DateTimeOffset date)
    {
        return DateOnly.FromDateTime(date.UtcDateTime);
    }

    [SystemPure]
    [JetBrainsPure]
    public static TimeOnly ToTimeOnly(this DateTimeOffset date)
    {
        return TimeOnly.FromDateTime(date.DateTime);
    }

    [SystemPure]
    [JetBrainsPure]
    public static TimeOnly ToUtcTimeOnly(this DateTimeOffset date)
    {
        return TimeOnly.FromDateTime(date.UtcDateTime);
    }
}
