// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Golden fire lists across DST transitions. Each list is derived by hand from the zone's offsets, never from the code
/// under test: a fixed-time occurrence in a fall-back overlap fires on its first pass, a second pass fires only when the
/// expression's cadence continues next to the repeated window, and every occurrence a spring-forward gap removes fires
/// shifted forward by the gap, merging with any occurrence already at that instant.
/// </summary>
/// <remarks>
/// The lists run on synthetic zones that copy each real zone's 2026 rules, so they pass or fail on this code rather than
/// on the host's tzdata. Real zones appear only for base-offset changes a synthetic adjustment rule cannot express, and
/// each such row first asserts that the host's tzdata has the transition it depends on.
/// </remarks>
public sealed class CronScheduleCacheDstTests : TestBase
{
    private static readonly TimeSpan _Quarter = TimeSpan.FromMinutes(15);

    #region Cairo fall-back: 29 October 2026, 24:00 (+03:00) becomes 23:00 (+02:00) at 21:00Z

    [Fact]
    public void should_fire_every_fifteen_real_minutes_across_the_cairo_overlap()
    {
        var fired = _Walk(_Cairo(), "0 */15 * * * *", _Utc(2026, 10, 29, 19, 0), _Utc(2026, 10, 29, 23, 0));

        fired.Should().Equal(_Every(_Utc(2026, 10, 29, 19, 0), _Utc(2026, 10, 29, 23, 0), _Quarter));
        fired.Should().HaveCount(17);
    }

    [Fact]
    public void should_fire_an_hour_pinned_step_on_the_first_pass_only()
    {
        var fired = _Walk(_Cairo(), "0 */15 23 * * *", _Utc(2026, 10, 29, 18, 0), _Utc(2026, 10, 30, 18, 0));

        fired
            .Should()
            .Equal(
                _Utc(2026, 10, 29, 20, 0),
                _Utc(2026, 10, 29, 20, 15),
                _Utc(2026, 10, 29, 20, 30),
                _Utc(2026, 10, 29, 20, 45)
            );
    }

    [Fact]
    public void should_fire_both_passes_when_a_day_of_week_field_keeps_the_cadence()
    {
        // 23:00 (+02:00) is still Thursday, so the interval continues through the repeated hour and stops at 22:00Z,
        // which is Friday 00:00 local.
        var fired = _Walk(_Cairo(), "0 */15 * * * 4", _Utc(2026, 10, 29, 19, 0), _Utc(2026, 10, 29, 23, 0));

        fired.Should().Equal(_Every(_Utc(2026, 10, 29, 19, 0), _Utc(2026, 10, 29, 21, 45), _Quarter));
    }

    [Fact]
    public void should_fire_both_passes_of_an_hourly_occurrence()
    {
        var cairo = _Cairo();

        _Walk(cairo, "0 0 * * * *", _Utc(2026, 10, 29, 19, 0), _Utc(2026, 10, 29, 23, 0))
            .Should()
            .Equal(_Every(_Utc(2026, 10, 29, 19, 0), _Utc(2026, 10, 29, 23, 0), TimeSpan.FromHours(1)));

        _Walk(cairo, "0 0 22-23 * * *", _Utc(2026, 10, 29, 18, 0), _Utc(2026, 10, 29, 23, 0))
            .Should()
            .Equal(_Utc(2026, 10, 29, 19, 0), _Utc(2026, 10, 29, 20, 0), _Utc(2026, 10, 29, 21, 0));
    }

    [Fact]
    public void should_fire_a_fixed_time_in_the_overlap_once_on_the_first_pass()
    {
        var fired = _Walk(_Cairo(), "0 30 23 * * *", _Utc(2026, 10, 29, 18, 0), _Utc(2026, 10, 31, 0, 0));

        fired.Should().Equal(_Utc(2026, 10, 29, 20, 30), _Utc(2026, 10, 30, 21, 30));
    }

    [Fact]
    public void should_fire_every_second_through_both_passes_and_compute_the_cadence_once_per_transition()
    {
        var cache = _Cairo();
        var from = _Utc(2026, 10, 29, 20, 59);
        var to = _Utc(2026, 10, 29, 21, 1);

        _Walk(cache, "* * * * * *", from, to).Should().Equal(_Every(from, to, TimeSpan.FromSeconds(1)));
        var verdicts = cache.CadenceVerdictCount;

        _Walk(cache, "* * * * * *", from, to).Should().HaveCount(121);

        verdicts.Should().Be(1);
        cache
            .CadenceVerdictCount.Should()
            .Be(verdicts, "the verdict is cached per transition, not recomputed per call");
    }

    public static TheoryData<DateTime> CairoOverlapCursors()
    {
        var cursors = new TheoryData<DateTime>();

        for (
            var cursor = _Utc(2026, 10, 29, 19, 40);
            cursor <= _Utc(2026, 10, 29, 21, 20);
            cursor = cursor.AddMinutes(1)
        )
        {
            cursors.Add(cursor);
        }

        return cursors;
    }

    [Theory]
    [MemberData(nameof(CairoOverlapCursors))]
    public void should_return_the_next_member_of_the_overlap_list_from_any_cursor(DateTime cursor)
    {
        var expected = _Every(_Utc(2026, 10, 29, 19, 0), _Utc(2026, 10, 29, 23, 0), _Quarter).First(x => x > cursor);

        var next = _Cairo().GetNextOccurrenceOrDefault("0 */15 * * * *", cursor);

        next.Should().Be(expected);
        next!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void should_continue_on_the_second_pass_from_a_cursor_exactly_at_the_transition()
    {
        _Cairo()
            .GetNextOccurrenceOrDefault("0 */15 * * * *", _Utc(2026, 10, 29, 21, 0))
            .Should()
            .Be(_Utc(2026, 10, 29, 21, 15));
    }

    #endregion

    #region Cairo spring-forward: 24 April 2026, 00:00 (+02:00) becomes 01:00 (+03:00) at 22:00Z on 23 April

    [Fact]
    public void should_fire_every_occurrence_pinned_to_the_missing_hour_shifted_by_the_gap()
    {
        var fired = _Walk(_Cairo(), "0 */15 0 * * *", _Utc(2026, 4, 23, 18, 0), _Utc(2026, 4, 24, 18, 0));

        fired
            .Should()
            .Equal(
                _Utc(2026, 4, 23, 22, 0),
                _Utc(2026, 4, 23, 22, 15),
                _Utc(2026, 4, 23, 22, 30),
                _Utc(2026, 4, 23, 22, 45)
            );
    }

    [Fact]
    public void should_merge_shifted_occurrences_with_the_ones_already_at_that_instant()
    {
        var fired = _Walk(_Cairo(), "0 */15 * * * *", _Utc(2026, 4, 23, 21, 0), _Utc(2026, 4, 23, 23, 0));

        fired.Should().Equal(_Every(_Utc(2026, 4, 23, 21, 0), _Utc(2026, 4, 23, 23, 0), _Quarter));
    }

    [Fact]
    public void should_shift_a_single_missing_occurrence_by_the_gap()
    {
        var cairo = _Cairo();

        _Walk(cairo, "0 30 0 * * *", _Utc(2026, 4, 23, 18, 0), _Utc(2026, 4, 25, 0, 0))
            .Should()
            .Equal(_Utc(2026, 4, 23, 22, 30), _Utc(2026, 4, 24, 21, 30));

        _Walk(cairo, "0 0 0 * * *", _Utc(2026, 4, 23, 18, 0), _Utc(2026, 4, 25, 0, 0))
            .Should()
            .Equal(_Utc(2026, 4, 23, 22, 0), _Utc(2026, 4, 24, 21, 0));
    }

    #endregion

    #region New York: spring 8 March 2026 at 07:00Z, fall 1 November 2026 at 06:00Z

    [Fact]
    public void should_fire_the_second_pass_when_the_cadence_continues_after_the_overlap()
    {
        var fired = _Walk(_NewYork(), "0 30 1,2 * * *", _Utc(2026, 11, 1, 4, 0), _Utc(2026, 11, 2, 4, 0));

        fired.Should().Equal(_Utc(2026, 11, 1, 5, 30), _Utc(2026, 11, 1, 6, 30), _Utc(2026, 11, 1, 7, 30));
    }

    [Fact]
    public void should_not_fire_the_second_pass_when_the_cadence_skips_the_neighbouring_windows()
    {
        var fired = _Walk(_NewYork(), "0 0 1/2 * * *", _Utc(2026, 11, 1, 4, 0), _Utc(2026, 11, 1, 9, 0));

        fired.Should().Equal(_Utc(2026, 11, 1, 5, 0), _Utc(2026, 11, 1, 8, 0));
    }

    [Fact]
    public void should_neither_duplicate_nor_lose_an_interval_across_the_new_york_gap()
    {
        var newYork = _NewYork();

        _Walk(newYork, "0 */15 * * * *", _Utc(2026, 3, 8, 6, 0), _Utc(2026, 3, 8, 8, 0))
            .Should()
            .Equal(_Every(_Utc(2026, 3, 8, 6, 0), _Utc(2026, 3, 8, 8, 0), _Quarter));

        newYork.GetNextOccurrenceOrDefault("0 30 2 * * *", _Utc(2026, 3, 8, 5, 0)).Should().Be(_Utc(2026, 3, 8, 7, 30));
    }

    #endregion

    #region Lord Howe: half-hour shifts; fall 5 April 2026 at 15:00Z on 4 April, spring 4 October 2026 at 15:30Z on 3 October

    [Fact]
    public void should_apply_the_cadence_rule_to_a_half_hour_overlap()
    {
        var lordHowe = _LordHowe();

        _Walk(lordHowe, "0 45 * * * *", _Utc(2026, 4, 4, 14, 0), _Utc(2026, 4, 4, 17, 0))
            .Should()
            .Equal(_Utc(2026, 4, 4, 14, 45), _Utc(2026, 4, 4, 16, 15));

        _Walk(lordHowe, "0 */15 1 * * *", _Utc(2026, 4, 4, 13, 0), _Utc(2026, 4, 5, 13, 0))
            .Should()
            .Equal(
                _Utc(2026, 4, 4, 14, 0),
                _Utc(2026, 4, 4, 14, 15),
                _Utc(2026, 4, 4, 14, 30),
                _Utc(2026, 4, 4, 14, 45),
                _Utc(2026, 4, 4, 15, 0),
                _Utc(2026, 4, 4, 15, 15)
            );
    }

    [Fact]
    public void should_return_a_shifted_occurrence_after_an_existing_one_that_precedes_it_in_utc()
    {
        // 02:20 does not exist and shifts to 02:50 (15:50Z); 02:40 exists and maps to 15:40Z, so the later local
        // occurrence fires first.
        var fired = _Walk(_LordHowe(), "0 20,40 2 * * *", _Utc(2026, 10, 3, 15, 0), _Utc(2026, 10, 4, 15, 0));

        fired.Should().Equal(_Utc(2026, 10, 3, 15, 40), _Utc(2026, 10, 3, 15, 50));
    }

    #endregion

    #region Santiago: fall 5 April 2026 at 03:00Z, spring 6 September 2026 at 04:00Z

    [Fact]
    public void should_apply_the_overlap_rule_at_a_midnight_fall_back()
    {
        var santiago = _Santiago();

        _Walk(santiago, "0 */15 * * * *", _Utc(2026, 4, 5, 1, 0), _Utc(2026, 4, 5, 5, 0))
            .Should()
            .Equal(_Every(_Utc(2026, 4, 5, 1, 0), _Utc(2026, 4, 5, 5, 0), _Quarter));

        _Walk(santiago, "0 */15 23 * * *", _Utc(2026, 4, 4, 12, 0), _Utc(2026, 4, 5, 12, 0))
            .Should()
            .Equal(_Utc(2026, 4, 5, 2, 0), _Utc(2026, 4, 5, 2, 15), _Utc(2026, 4, 5, 2, 30), _Utc(2026, 4, 5, 2, 45));
    }

    [Fact]
    public void should_apply_the_gap_rule_at_a_midnight_spring_forward()
    {
        var santiago = _Santiago();

        _Walk(santiago, "0 */15 0 * * *", _Utc(2026, 9, 5, 12, 0), _Utc(2026, 9, 6, 12, 0))
            .Should()
            .Equal(_Utc(2026, 9, 6, 4, 0), _Utc(2026, 9, 6, 4, 15), _Utc(2026, 9, 6, 4, 30), _Utc(2026, 9, 6, 4, 45));

        santiago.GetNextOccurrenceOrDefault("0 0 0 * * *", _Utc(2026, 9, 5, 12, 0)).Should().Be(_Utc(2026, 9, 6, 4, 0));
    }

    #endregion

    #region Sparse schedules and bounds

    [Fact]
    public void should_map_a_sparse_occurrence_with_the_offset_in_effect_on_its_own_date()
    {
        _NewYork()
            .GetNextOccurrenceOrDefault("0 0 12 1 6 *", _Utc(2026, 11, 1, 6, 30))
            .Should()
            .Be(_Utc(2027, 6, 1, 16, 0));

        _Cairo()
            .GetNextOccurrenceOrDefault("0 0 12 1 6 *", _Utc(2026, 10, 29, 20, 30))
            .Should()
            .Be(_Utc(2027, 6, 1, 9, 0));
    }

    [Fact]
    public void should_return_null_for_an_expression_that_never_fires_in_a_dst_zone()
    {
        _Cairo().GetNextOccurrenceOrDefault("0 0 0 30 2 *", _Utc(2026, 10, 29, 20, 30)).Should().BeNull();
    }

    [Fact]
    public void should_return_null_without_overflow_for_a_cursor_near_the_end_of_time()
    {
        var cursor = DateTime.SpecifyKind(DateTime.MaxValue.AddSeconds(-1), DateTimeKind.Utc);

        _Cairo().GetNextOccurrenceOrDefault("0 */15 * * * *", cursor).Should().BeNull();
    }

    #endregion

    #region Real zones whose transitions synthetic rules cannot express

    [Fact]
    public void should_keep_instants_increasing_across_apia_skipped_day()
    {
        var apia = _RealZone(
            "Pacific/Apia",
            _Utc(2011, 12, 30, 10, 0),
            TimeSpan.FromHours(-10),
            TimeSpan.FromHours(14)
        );

        // 30 December's 09:00 shifts by the 24-hour gap onto 31 December's 09:00 (+14:00), which is 19:00Z on the 30th.
        _Walk(apia, "0 0 9 * * *", _Utc(2011, 12, 28, 0, 0), _Utc(2012, 1, 1, 0, 0))
            .Should()
            .Equal(
                _Utc(2011, 12, 28, 19, 0),
                _Utc(2011, 12, 29, 19, 0),
                _Utc(2011, 12, 30, 19, 0),
                _Utc(2011, 12, 31, 19, 0)
            );
    }

    [Fact]
    public void should_fire_both_passes_of_troll_two_hour_overlap()
    {
        var troll = _RealZone("Antarctica/Troll", _Utc(2026, 10, 25, 1, 0), TimeSpan.FromHours(2), TimeSpan.Zero);

        _Walk(troll, "0 */15 * * * *", _Utc(2026, 10, 24, 23, 0), _Utc(2026, 10, 25, 3, 0))
            .Should()
            .Equal(_Every(_Utc(2026, 10, 24, 23, 0), _Utc(2026, 10, 25, 3, 0), _Quarter));
    }

    [Fact]
    public void should_neither_duplicate_nor_lose_an_interval_across_troll_two_hour_gap()
    {
        var troll = _RealZone("Antarctica/Troll", _Utc(2026, 3, 29, 1, 0), TimeSpan.Zero, TimeSpan.FromHours(2));

        _Walk(troll, "0 */15 * * * *", _Utc(2026, 3, 28, 23, 0), _Utc(2026, 3, 29, 3, 0))
            .Should()
            .Equal(_Every(_Utc(2026, 3, 28, 23, 0), _Utc(2026, 3, 29, 3, 0), _Quarter));
    }

    [Fact]
    public void should_fire_both_passes_of_volgograd_base_offset_change()
    {
        var volgograd = _RealZone(
            "Europe/Volgograd",
            _Utc(2020, 12, 26, 22, 0),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(3)
        );

        _Walk(volgograd, "0 0 * * * *", _Utc(2020, 12, 26, 19, 0), _Utc(2020, 12, 26, 23, 0))
            .Should()
            .Equal(_Every(_Utc(2020, 12, 26, 19, 0), _Utc(2020, 12, 26, 23, 0), TimeSpan.FromHours(1)));
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Every instant the schedule fires in [<paramref name="firstUtc"/>, <paramref name="lastUtc"/>], walked the way the
    /// scheduler walks it: each lookup starts at the previous result. Asserts each result is strictly after its cursor.
    /// </summary>
    private static List<DateTime> _Walk(CronScheduleCache cache, string expression, DateTime firstUtc, DateTime lastUtc)
    {
        var fired = new List<DateTime>();
        var cursor = firstUtc.AddTicks(-1);

        while (cache.GetNextOccurrenceOrDefault(expression, cursor) is { } next && next <= lastUtc)
        {
            next.Should().BeAfter(cursor, "every next occurrence is strictly after the cursor it was computed from");
            fired.Add(next);
            cursor = next;
        }

        return fired;
    }

    private static List<DateTime> _Every(DateTime firstUtc, DateTime lastUtc, TimeSpan step)
    {
        var instants = new List<DateTime>();

        for (var instant = firstUtc; instant <= lastUtc; instant += step)
        {
            instants.Add(instant);
        }

        return instants;
    }

    private static DateTime _Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    private static CronScheduleCache _RealZone(
        string id,
        DateTime transitionUtc,
        TimeSpan offsetBefore,
        TimeSpan offsetAfter
    )
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
        var because = $"the host's tzdata must place {id}'s transition at {transitionUtc:O}";

        zone.GetUtcOffset(transitionUtc.AddSeconds(-1)).Should().Be(offsetBefore, because);
        zone.GetUtcOffset(transitionUtc).Should().Be(offsetAfter, because);

        return new CronScheduleCache(zone);
    }

    // Each synthetic zone copies its real zone's rules year by year. A rule's start time is standard local time and its
    // end time daylight local time, as TimeZoneInfo defines them.
    private static CronScheduleCache _Cairo()
    {
        return _Synthetic(
            "Headless.Test.Cairo",
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(1),
            (2026, new DateTime(1, 4, 24, 0, 0, 0), new DateTime(1, 10, 30, 0, 0, 0)),
            (2027, new DateTime(1, 4, 30, 0, 0, 0), new DateTime(1, 10, 29, 0, 0, 0))
        );
    }

    private static CronScheduleCache _NewYork()
    {
        return _Synthetic(
            "Headless.Test.NewYork",
            TimeSpan.FromHours(-5),
            TimeSpan.FromHours(1),
            (2026, new DateTime(1, 3, 8, 2, 0, 0), new DateTime(1, 11, 1, 2, 0, 0)),
            (2027, new DateTime(1, 3, 14, 2, 0, 0), new DateTime(1, 11, 7, 2, 0, 0))
        );
    }

    private static CronScheduleCache _LordHowe()
    {
        return _Synthetic(
            "Headless.Test.LordHowe",
            new TimeSpan(10, 30, 0),
            TimeSpan.FromMinutes(30),
            (2026, new DateTime(1, 10, 4, 2, 0, 0), new DateTime(1, 4, 5, 2, 0, 0))
        );
    }

    private static CronScheduleCache _Santiago()
    {
        return _Synthetic(
            "Headless.Test.Santiago",
            TimeSpan.FromHours(-4),
            TimeSpan.FromHours(1),
            (2026, new DateTime(1, 9, 6, 0, 0, 0), new DateTime(1, 4, 5, 0, 0, 0))
        );
    }

    private static CronScheduleCache _Synthetic(
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

        var zone = TimeZoneInfo.CreateCustomTimeZone(id, baseOffset, id, id + " Standard", id + " Daylight", rules);

        return new CronScheduleCache(zone);
    }

    private static TimeZoneInfo.TransitionTime _FixedDate(DateTime monthDayTime)
    {
        return TimeZoneInfo.TransitionTime.CreateFixedDateRule(
            new DateTime(1, 1, 1, monthDayTime.Hour, monthDayTime.Minute, 0),
            monthDayTime.Month,
            monthDayTime.Day
        );
    }

    #endregion
}
