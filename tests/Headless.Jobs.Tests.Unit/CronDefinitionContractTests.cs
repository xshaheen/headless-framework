// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Tests;

public sealed class CronDefinitionContractTests
{
    [Fact]
    public void should_default_to_active_global_timezone_and_initial_revision_when_cron_definition_is_created()
    {
        var definition = new CronJobEntity();

        definition.IsPaused.Should().BeFalse();
        definition.TimeZoneId.Should().BeNull();
        definition.ScheduleRevision.Should().Be(0);
    }

    [Fact]
    public void should_use_global_timezone_fallback_when_recurring_options_are_defaulted()
    {
        new RecurringJobOptions().TimeZoneId.Should().BeNull();
    }

    [Fact]
    public void should_preserve_iana_timezone_id_when_recurring_options_supply_one()
    {
        const string timeZoneId = "America/New_York";

        new RecurringJobOptions { TimeZoneId = timeZoneId }
            .TimeZoneId.Should()
            .Be(timeZoneId);
    }
}
