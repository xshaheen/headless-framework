// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>
/// Synthetic zones that copy real zones' DST rules year by year, so a DST test passes or fails on the code under test
/// rather than on whatever tzdata the host ships.
/// </summary>
/// <remarks>
/// A rule's start time is standard local time and its end time daylight local time, as <see cref="TimeZoneInfo"/>
/// defines them.
/// </remarks>
internal static class CronTestZones
{
    /// <summary>Africa/Cairo: falls back at 21:00Z on 29 October 2026 and springs forward at 22:00Z on 23 April 2026.</summary>
    public static TimeZoneInfo Cairo()
    {
        return _Synthetic(
            "Headless.Test.Cairo",
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(1),
            (2026, new DateTime(1, 4, 24, 0, 0, 0), new DateTime(1, 10, 30, 0, 0, 0)),
            (2027, new DateTime(1, 4, 30, 0, 0, 0), new DateTime(1, 10, 29, 0, 0, 0))
        );
    }

    /// <summary>America/New_York: springs forward at 07:00Z on 8 March 2026 and falls back at 06:00Z on 1 November 2026.</summary>
    public static TimeZoneInfo NewYork()
    {
        return _Synthetic(
            "Headless.Test.NewYork",
            TimeSpan.FromHours(-5),
            TimeSpan.FromHours(1),
            (2026, new DateTime(1, 3, 8, 2, 0, 0), new DateTime(1, 11, 1, 2, 0, 0)),
            (2027, new DateTime(1, 3, 14, 2, 0, 0), new DateTime(1, 11, 7, 2, 0, 0))
        );
    }

    /// <summary>Australia/Lord_Howe: half-hour shifts, at 15:00Z on 4 April 2026 and 15:30Z on 3 October 2026.</summary>
    public static TimeZoneInfo LordHowe()
    {
        return _Synthetic(
            "Headless.Test.LordHowe",
            new TimeSpan(10, 30, 0),
            TimeSpan.FromMinutes(30),
            (2026, new DateTime(1, 10, 4, 2, 0, 0), new DateTime(1, 4, 5, 2, 0, 0))
        );
    }

    /// <summary>America/Santiago: falls back at 03:00Z on 5 April 2026 and springs forward at 04:00Z on 6 September 2026.</summary>
    public static TimeZoneInfo Santiago()
    {
        return _Synthetic(
            "Headless.Test.Santiago",
            TimeSpan.FromHours(-4),
            TimeSpan.FromHours(1),
            (2026, new DateTime(1, 9, 6, 0, 0, 0), new DateTime(1, 4, 5, 0, 0, 0))
        );
    }

    private static TimeZoneInfo _Synthetic(
        string id,
        TimeSpan baseOffset,
        TimeSpan daylightDelta,
        params (int Year, DateTime Start, DateTime End)[] years
    )
    {
        var rules = years
            .Select(year =>
                TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                    new DateTime(year.Year, 1, 1),
                    new DateTime(year.Year, 12, 31),
                    daylightDelta,
                    _FixedDate(year.Start),
                    _FixedDate(year.End)
                )
            )
            .ToArray();

        return TimeZoneInfo.CreateCustomTimeZone(id, baseOffset, id, id + " Standard", id + " Daylight", rules);
    }

    private static TimeZoneInfo.TransitionTime _FixedDate(DateTime monthDayTime)
    {
        return TimeZoneInfo.TransitionTime.CreateFixedDateRule(
            new DateTime(1, 1, 1, monthDayTime.Hour, monthDayTime.Minute, 0),
            monthDayTime.Month,
            monthDayTime.Day
        );
    }
}
