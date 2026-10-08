// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SequenceNumberTests : TestBase
{
    private static readonly DateOnly _Date = new(2026, 10, 8);

    [Theory]
    [InlineData("REC-{yyyy}-{seq:D6}", 42, "REC-2026-000042")]
    [InlineData("{yy}{MM}{dd}/{seq}", 7, "261008/7")]
    [InlineData("INV {{{seq:D3}}}", 5, "INV {005}")]
    [InlineData("plain", 1, "plain")]
    [InlineData("{seq:D2}", 1234, "1234")]
    public void should_format_each_token_and_keep_literal_text(string template, long value, string expected)
    {
        var policy = new SequencePolicy { Format = template };

        SequenceNumberFormat.Get(template).Format(policy, value, _Date).Should().Be(expected);
    }

    [Theory]
    [InlineData("{fy}", 1, "2026")]
    [InlineData("{fy}", 7, "2026")]
    [InlineData("{fy}", 11, "2025")]
    public void should_format_the_year_the_fiscal_year_starts_in(string template, int startMonth, string expected)
    {
        var policy = new SequencePolicy { Format = template, FiscalYearStartMonth = startMonth };

        SequenceNumberFormat.Get(template).Format(policy, 1, _Date).Should().Be(expected);
    }

    [Theory]
    [InlineData("{year}")]
    [InlineData("{seq:D0}")]
    [InlineData("{seq:D20}")]
    [InlineData("{seq:X4}")]
    [InlineData("REC-{seq")]
    [InlineData("REC}-{seq}")]
    public void should_reject_an_unknown_token_or_an_unbalanced_brace(string template)
    {
        SequenceNumberFormat.TryValidate(template, out var error).Should().BeFalse();
        error.Should().Contain(template);
    }

    [Theory]
    [InlineData(SequenceReset.Never, 1, null)]
    [InlineData(SequenceReset.Year, 1, "2026")]
    [InlineData(SequenceReset.Month, 1, "2026-10")]
    [InlineData(SequenceReset.Day, 1, "2026-10-08")]
    [InlineData(SequenceReset.FiscalYear, 7, "FY2026")]
    [InlineData(SequenceReset.FiscalYear, 11, "FY2025")]
    public void should_choose_the_partition_of_the_reset_period(SequenceReset reset, int startMonth, string? expected)
    {
        var policy = new SequencePolicy { Reset = reset, FiscalYearStartMonth = startMonth };

        SequenceNumberFormat.Partition(policy, _Date).Should().Be(expected);
    }

    [Fact]
    public void should_decide_the_issue_date_at_the_policy_time_zone_midnight()
    {
        // 22:30 UTC on 7 October is already 8 October in Cairo (UTC+3 in October 2026).
        var instant = new DateTimeOffset(2026, 10, 7, 22, 30, 0, TimeSpan.Zero);
        var cairo = new SequencePolicy { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo") };

        SequenceNumberFormat.IssuedOn(new SequencePolicy(), instant).Should().Be(new DateOnly(2026, 10, 7));
        SequenceNumberFormat.IssuedOn(cairo, instant).Should().Be(new DateOnly(2026, 10, 8));
    }

    [Fact]
    public void should_report_a_bad_template_and_fiscal_month_through_options_validation()
    {
        var options = new SequencesOptions();
        options.Policies["receipt"] = new SequencePolicy { Format = "R-{nope}", FiscalYearStartMonth = 13 };

        var result = new SequencesOptionsValidator().Validate(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("{nope}", StringComparison.Ordinal));
        result.Errors.Should().Contain(e => e.PropertyName.EndsWith("FiscalYearStartMonth", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_take_a_fast_document_number_in_the_period_partition_and_format_it()
    {
        // given
        var context = new SequenceTestContext();
        context.Tenant.Id = "t1";
        context.Options.Policies["order"] = new SequencePolicy
        {
            Format = "ORD-{yyyy}{MM}-{seq:D4}",
            Reset = SequenceReset.Month,
        };

        // when
        var number = await context.Generator.NextNumberAsync("order", AbortToken);

        // then
        number.Should().Be(new SequenceNumber(1, "2026-10", new DateOnly(2026, 10, 8), "ORD-202610-0001"));
        number.ToString().Should().Be("ORD-202610-0001");
        await context.Store.Received(1).IncrementAsync(new SequenceKey("t1", "order", "2026-10"), 1, 1, AbortToken);
    }

    [Fact]
    public async Task should_take_a_gap_free_document_number_on_the_unit()
    {
        // given
        var context = new SequenceTestContext();
        context.Options.Policies["receipt"] = new SequencePolicy
        {
            Mode = SequenceMode.GapFree,
            Format = "REC-{fy}-{seq:D5}",
            Reset = SequenceReset.FiscalYear,
            FiscalYearStartMonth = 7,
            Start = 100,
        };
        var (unit, _) = SequenceTestContext.ActiveUnit();

        // when
        var number = await context.Feature.NextNumberAsync(unit, "receipt", AbortToken);

        // then
        number.Text.Should().Be("REC-2026-00100");
        number.Partition.Should().Be("FY2026");
        await context
            .Store.Received(1)
            .IncrementEnlistedAsync(unit, new SequenceKey("", "receipt", "FY2026"), 100, 1, AbortToken);
    }

    [Fact]
    public async Task should_format_the_bare_value_without_a_template()
    {
        var context = new SequenceTestContext();

        var number = await context.Generator.NextNumberAsync("ticket", AbortToken);

        number.Should().Be(new SequenceNumber(1, Partition: null, new DateOnly(2026, 10, 8), "1"));
    }
}
