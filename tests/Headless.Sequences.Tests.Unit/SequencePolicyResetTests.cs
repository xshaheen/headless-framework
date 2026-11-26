// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SequencePolicyResetTests : TestBase
{
    private static readonly TimeZoneInfo _Cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    [Fact]
    public void should_have_no_partition_and_no_reset_when_the_policy_never_resets()
    {
        var policy = new SequencePolicy();
        var instant = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

        policy.PartitionAt(instant).Should().BeNull();
        policy.NextResetAt(instant).Should().BeNull();
    }

    [Fact]
    public void should_reset_a_daily_counter_at_the_next_utc_midnight()
    {
        var policy = new SequencePolicy { Reset = SequenceReset.Day };
        var instant = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

        policy.PartitionAt(instant).Should().Be("2026-10-08");
        policy.NextResetAt(instant).Should().Be(new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void should_reset_a_monthly_counter_on_the_first_of_the_next_month_across_a_year_end()
    {
        var policy = new SequencePolicy { Reset = SequenceReset.Month };
        var instant = new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero);

        policy.PartitionAt(instant).Should().Be("2026-12");
        policy.NextResetAt(instant).Should().Be(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void should_reset_a_yearly_counter_on_january_first_of_the_next_year()
    {
        var policy = new SequencePolicy { Reset = SequenceReset.Year };
        var instant = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

        policy.PartitionAt(instant).Should().Be("2026");
        policy.NextResetAt(instant).Should().Be(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void should_reset_a_fiscal_year_counter_at_the_next_fiscal_year_start()
    {
        var policy = new SequencePolicy { Reset = SequenceReset.FiscalYear, FiscalYearStartMonth = 7 };
        var lastDay = new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);
        var firstDay = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        policy.PartitionAt(lastDay).Should().Be("FY2025");
        policy.NextResetAt(lastDay).Should().Be(firstDay);
        policy.PartitionAt(firstDay).Should().Be("FY2026");
        policy.NextResetAt(firstDay).Should().Be(new DateTimeOffset(2027, 7, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void should_reset_at_the_first_valid_local_instant_when_daylight_saving_skips_midnight()
    {
        var springForward = _FirstSkippedMidnight(_Cairo, 2026);
        var policy = new SequencePolicy { Reset = SequenceReset.Day, TimeZone = _Cairo };

        // The evening before the skipped midnight, in local standard time (UTC+2).
        var evening = new DateTimeOffset(
            springForward.AddDays(-1).ToDateTime(new TimeOnly(20, 0)),
            TimeSpan.FromHours(2)
        );

        var reset = policy.NextResetAt(evening);

        reset.Should().NotBeNull();
        policy
            .PartitionAt(reset!.Value)
            .Should()
            .Be(springForward.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        policy.PartitionAt(reset.Value.AddTicks(-1)).Should().Be(policy.PartitionAt(evening));
        TimeZoneInfo.ConvertTime(reset.Value, _Cairo).TimeOfDay.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("UTC", SequenceReset.Day, 1)]
    [InlineData("UTC", SequenceReset.Month, 1)]
    [InlineData("Africa/Cairo", SequenceReset.Day, 1)]
    [InlineData("Africa/Cairo", SequenceReset.Month, 1)]
    [InlineData("Africa/Cairo", SequenceReset.Year, 1)]
    [InlineData("Africa/Cairo", SequenceReset.FiscalYear, 4)]
    [InlineData("UTC", SequenceReset.FiscalYear, 4)]
    public void should_reset_after_the_instant_into_a_new_partition_for_every_hour_of_two_years(
        string zone,
        SequenceReset reset,
        int fiscalYearStartMonth
    )
    {
        var policy = new SequencePolicy
        {
            Reset = reset,
            FiscalYearStartMonth = fiscalYearStartMonth,
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(zone),
        };
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        for (var hour = 0; hour < 2 * 366 * 24; hour++)
        {
            var instant = start.AddHours(hour);
            var next = policy.NextResetAt(instant);

            next.Should().NotBeNull();
            next!.Value.Should().BeAfter(instant);
            policy.PartitionAt(next.Value).Should().NotBe(policy.PartitionAt(instant));
            policy.PartitionAt(next.Value.AddTicks(-1)).Should().Be(policy.PartitionAt(instant));
        }
    }

    private static DateOnly _FirstSkippedMidnight(TimeZoneInfo zone, int year)
    {
        for (var date = new DateOnly(year, 1, 1); date.Year == year; date = date.AddDays(1))
        {
            if (zone.IsInvalidTime(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified)))
            {
                return date;
            }
        }

        throw new InvalidOperationException($"{zone.Id} skips no local midnight in {year}.");
    }
}
