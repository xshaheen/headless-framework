// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class RecurringAttributeTests : TestBase
{
    [Fact]
    public void should_set_cron_expression_when_constructed()
    {
        // given
        const string cron = "0 0 */6 * * *";

        // when
        var attr = new RecurringAttribute(cron);

        // then
        attr.CronExpression.Should().Be(cron);
    }

    [Fact]
    public void should_default_skip_if_running_to_true_when_constructed()
    {
        // when
        var attr = new RecurringAttribute("* * * * * *");

        // then
        attr.SkipIfRunning.Should().BeTrue();
    }

    [Fact]
    public void should_default_name_to_null_when_not_set()
    {
        // when
        var attr = new RecurringAttribute("* * * * * *");

        // then
        attr.Name.Should().BeNull();
    }

    [Fact]
    public void should_default_time_zone_to_null_when_not_set()
    {
        // when
        var attr = new RecurringAttribute("* * * * * *");

        // then
        attr.TimeZone.Should().BeNull();
    }

    [Fact]
    public void should_default_retry_intervals_to_null_when_not_set()
    {
        // when
        var attr = new RecurringAttribute("* * * * * *");

        // then
        attr.RetryIntervals.Should().BeNull();
    }

    [Fact]
    public void should_set_name_when_provided()
    {
        // when
        var attr = new RecurringAttribute("0 0 * * * *") { Name = "daily-report" };

        // then
        attr.Name.Should().Be("daily-report");
    }

    [Fact]
    public void should_set_time_zone_when_provided()
    {
        // when
        var attr = new RecurringAttribute("0 0 9 * * *") { TimeZone = "America/New_York" };

        // then
        attr.TimeZone.Should().Be("America/New_York");
    }

    [Fact]
    public void should_set_retry_intervals_when_provided()
    {
        // given
        int[] intervals = [5, 30, 120];

        // when
        var attr = new RecurringAttribute("0 0 * * * *") { RetryIntervals = intervals };

        // then
        attr.RetryIntervals.Should().BeEquivalentTo(intervals);
    }

    [Fact]
    public void should_set_skip_if_running_to_false_when_overridden()
    {
        // when
        var attr = new RecurringAttribute("0 0 * * * *") { SkipIfRunning = false };

        // then
        attr.SkipIfRunning.Should().BeFalse();
    }

    [Fact]
    public void should_set_all_properties_when_fully_configured()
    {
        // given
        const string cron = "0 30 2 * * *";
        int[] intervals = [10, 60];

        // when
        var attr = new RecurringAttribute(cron)
        {
            Name = "nightly-cleanup",
            TimeZone = "Europe/London",
            RetryIntervals = intervals,
            SkipIfRunning = false,
        };

        // then
        attr.CronExpression.Should().Be(cron);
        attr.Name.Should().Be("nightly-cleanup");
        attr.TimeZone.Should().Be("Europe/London");
        attr.RetryIntervals.Should().BeEquivalentTo(intervals);
        attr.SkipIfRunning.Should().BeFalse();
    }

    [Fact]
    public void should_be_sealed_class()
    {
        // then
        typeof(RecurringAttribute).Should().BeSealed();
    }

    [Fact]
    public void should_derive_from_attribute()
    {
        // then
        typeof(RecurringAttribute).Should().BeDerivedFrom<Attribute>();
    }

    [Fact]
    public void should_target_class_only_when_applied()
    {
        // when
        var usage = typeof(RecurringAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        // then
        usage.Should().NotBeNull();
        usage!.ValidOn.Should().Be(AttributeTargets.Class);
    }

    [Fact]
    public void should_not_allow_multiple_when_applied()
    {
        // when
        var usage = typeof(RecurringAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        // then
        usage!.AllowMultiple.Should().BeFalse();
    }

    [Fact]
    public void should_inherit_when_applied()
    {
        // when
        var usage = typeof(RecurringAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        // then
        usage!.Inherited.Should().BeTrue();
    }
}
