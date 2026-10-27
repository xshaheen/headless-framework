// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// One change of a zone's UTC offset: from <see cref="OffsetBefore"/> to <see cref="OffsetAfter"/> at
/// <see cref="InstantUtc"/>. A larger offset after is a spring-forward gap; a smaller one is a fall-back overlap.
/// </summary>
/// <remarks>
/// Transitions are found from changes in <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/> on the UTC timeline, never
/// from <see cref="TimeZoneInfo.IsInvalidTime"/> or <see cref="TimeZoneInfo.IsAmbiguousTime(DateTime)"/>: those report
/// only daylight saving, and miss a change of the zone's base offset such as Samoa skipping 30 December 2011 or
/// Volgograd moving back an hour on 27 December 2020.
/// <para>
/// The search assumes a zone changes its offset at most once within <see cref="SearchRadius"/>, and that every
/// transition falls on a whole second. Both hold for every zone in the IANA database.
/// </para>
/// </remarks>
internal readonly record struct CronZoneTransition(DateTime InstantUtc, TimeSpan OffsetBefore, TimeSpan OffsetAfter)
{
    /// <summary>
    /// Wider than the largest shift any zone has made (Samoa's 24 hours), so a transition's gap or repeated window
    /// always ends within this distance of the transition.
    /// </summary>
    public static readonly TimeSpan SearchRadius = TimeSpan.FromHours(26);

    public bool IsGap => OffsetAfter > OffsetBefore;

    /// <summary>The local wall-clock time the clock shows right after the transition.</summary>
    public DateTime LocalAfter => Add(InstantUtc, OffsetAfter);

    public static TimeSpan OffsetAt(TimeZoneInfo zone, DateTime utc)
    {
        return zone.GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }

    /// <summary>The latest transition in (<paramref name="utc"/> − <see cref="SearchRadius"/>, <paramref name="utc"/>].</summary>
    public static CronZoneTransition? FindPrevious(TimeZoneInfo zone, DateTime utc)
    {
        var from = Add(utc, -SearchRadius);

        return OffsetAt(zone, from) == OffsetAt(zone, utc) ? null : _Locate(zone, from, utc);
    }

    /// <summary>The first transition in (<paramref name="afterUtc"/>, <paramref name="untilUtc"/>].</summary>
    /// <remarks>
    /// Steps forward one <see cref="SearchRadius"/> at a time, so two transitions that cancel each other out between
    /// distant endpoints are still seen. Only a schedule whose next occurrence is far away pays for many steps.
    /// </remarks>
    public static CronZoneTransition? FindNext(TimeZoneInfo zone, DateTime afterUtc, DateTime untilUtc)
    {
        var offset = OffsetAt(zone, afterUtc);
        var from = afterUtc;

        while (from < untilUtc)
        {
            var to = Add(from, SearchRadius);

            if (to > untilUtc)
            {
                to = untilUtc;
            }

            if (OffsetAt(zone, to) != offset)
            {
                return _Locate(zone, from, to);
            }

            from = to;
        }

        return null;
    }

    /// <summary>Adds <paramref name="span"/>, clamping at <see cref="DateTime.MinValue"/> and <see cref="DateTime.MaxValue"/>.</summary>
    public static DateTime Add(DateTime instant, TimeSpan span)
    {
        // Both operands are within ±DateTime.MaxValue.Ticks, so the sum cannot overflow a long.
        var ticks = Math.Clamp(instant.Ticks + span.Ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks);

        return new DateTime(ticks, instant.Kind);
    }

    // Binary search, to the second, for the single offset change between two instants whose offsets differ.
    private static CronZoneTransition _Locate(TimeZoneInfo zone, DateTime fromUtc, DateTime toUtc)
    {
        // Transitions fall on whole seconds, so flooring both ends keeps their offsets: the low end stays before the
        // transition and the high end stays at or after it.
        var low = fromUtc.Ticks / TimeSpan.TicksPerSecond;
        var high = toUtc.Ticks / TimeSpan.TicksPerSecond;
        var before = OffsetAt(zone, _Second(low));

        while (high - low > 1)
        {
            var middle = low + ((high - low) / 2);

            if (OffsetAt(zone, _Second(middle)) == before)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        var instant = _Second(high);

        return new CronZoneTransition(instant, before, OffsetAt(zone, instant));
    }

    private static DateTime _Second(long seconds)
    {
        return new DateTime(seconds * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }
}
