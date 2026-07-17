// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Tests;

public sealed class CronDefinitionContractTests
{
    [Fact]
    public void cron_definition_defaults_to_active_global_timezone_and_initial_revision()
    {
        var definition = new CronJobEntity();

        definition.IsPaused.Should().BeFalse();
        definition.TimeZoneId.Should().BeNull();
        definition.ScheduleRevision.Should().Be(0);
    }

    [Fact]
    public void recurring_options_default_to_the_global_timezone_fallback()
    {
        new RecurringJobOptions().TimeZoneId.Should().BeNull();
    }

    [Fact]
    public void recurring_options_preserve_the_supplied_iana_timezone_id()
    {
        const string timeZoneId = "America/New_York";

        new RecurringJobOptions { TimeZoneId = timeZoneId }
            .TimeZoneId.Should()
            .Be(timeZoneId);
    }
}
