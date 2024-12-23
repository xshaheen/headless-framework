// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

/// <summary>Provides a set of extension methods for operations on <see cref="DateTime"/>.</summary>
[PublicAPI]
public static class DateTimeExtensions
{
    [SystemPure]
    [JetBrainsPure]
    public static DateTime ClearTime(this DateTime dateTime)
    {
        return new(dateTime.Year, dateTime.Month, dateTime.Day);
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
        return new(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, date.Millisecond);
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
        return new(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, 0);
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
        return new(date.Year, date.Month, date.Day, date.Hour, date.Minute, 0, 0);
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
        return new(date.Year, date.Month, date.Day, date.Hour, 0, 0, 0);
    }

    [SystemPure]
    [JetBrainsPure]
    public static long ToUnixTimeSeconds(this DateTime date)
    {
        return new DateTimeOffset(date.ToUniversalTime()).ToUnixTimeSeconds();
    }

    [SystemPure]
    [JetBrainsPure]
    public static long ToUnixTimeMilliseconds(this DateTime date)
    {
        return new DateTimeOffset(date.ToUniversalTime()).ToUnixTimeMilliseconds();
    }
}
