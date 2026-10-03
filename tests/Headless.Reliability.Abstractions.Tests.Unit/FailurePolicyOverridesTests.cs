// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Reliability;
using Headless.Testing.Tests;

namespace Tests;

public sealed class FailurePolicyOverridesTests : TestBase
{
    private const string _Section = "Jobs:billing.invoice:FailurePolicy";

    [Fact]
    public void should_parse_every_setting_when_values_are_valid()
    {
        // given
        List<string> errors = [];
        (string Key, string Path, string? Value)[] settings =
        [
            ("ImmediateRetries", $"{_Section}:ImmediateRetries", "3"),
            ("DelayedRetries", $"{_Section}:DelayedRetries", "7"),
            ("DelayedInitialDelay", $"{_Section}:DelayedInitialDelay", "00:00:45"),
            ("DelayedMaxDelay", $"{_Section}:DelayedMaxDelay", "01:30:00"),
        ];

        // when
        var parsed = FailurePolicyOverrides.TryParse(settings, errors, out var overrides);

        // then
        parsed.Should().BeTrue();
        errors.Should().BeEmpty();
        overrides.Should().NotBeNull();
        overrides!.ImmediateRetries.Should().Be(3);
        overrides.DelayedRetries.Should().Be(7);
        overrides.DelayedInitialDelay.Should().Be(new TimeSpan(0, 0, 45));
        overrides.DelayedMaxDelay.Should().Be(new TimeSpan(1, 30, 0));
    }

    [Fact]
    public void should_match_keys_case_insensitively()
    {
        // given
        List<string> errors = [];
        (string Key, string Path, string? Value)[] settings =
        [
            ("immediateretries", "p:immediateretries", "1"),
            ("DELAYEDMAXDELAY", "p:DELAYEDMAXDELAY", "2.00:00:00"),
        ];

        // when
        var parsed = FailurePolicyOverrides.TryParse(settings, errors, out var overrides);

        // then
        parsed.Should().BeTrue();
        overrides!.ImmediateRetries.Should().Be(1);
        overrides.DelayedMaxDelay.Should().Be(new TimeSpan(2, 0, 0, 0));
        overrides.DelayedRetries.Should().BeNull();
        overrides.DelayedInitialDelay.Should().BeNull();
    }

    [Fact]
    public void should_return_empty_overrides_when_no_setting_is_given()
    {
        // given
        List<string> errors = [];

        // when
        var parsed = FailurePolicyOverrides.TryParse([], errors, out var overrides);

        // then
        parsed.Should().BeTrue();
        errors.Should().BeEmpty();
        overrides.Should().BeEquivalentTo(new FailurePolicyOverrides());
    }

    [Fact]
    public void should_leave_range_checks_to_the_definition_when_retries_are_out_of_range()
    {
        // given
        List<string> errors = [];
        (string Key, string Path, string? Value)[] settings =
        [
            ("ImmediateRetries", "p:ImmediateRetries", "-1"),
            ("DelayedRetries", "p:DelayedRetries", "1000"),
        ];

        // when
        var parsed = FailurePolicyOverrides.TryParse(settings, errors, out var overrides);

        // then
        parsed.Should().BeTrue();
        overrides!.ImmediateRetries.Should().Be(-1);
        overrides.DelayedRetries.Should().Be(1000);
    }

    [Theory]
    [InlineData("ImmediateRetries", "abc")]
    [InlineData("ImmediateRetries", "1.5")]
    [InlineData("DelayedRetries", "")]
    [InlineData("DelayedRetries", null)]
    public void should_report_an_integer_error_when_retries_do_not_parse(string key, string? value)
    {
        // given
        List<string> errors = [];
        var path = $"{_Section}:{key}";

        // when
        var parsed = FailurePolicyOverrides.TryParse([(key, path, value)], errors, out var overrides);

        // then
        parsed.Should().BeFalse();
        overrides.Should().BeNull();
        errors.Should().Equal($"Configuration '{path}' must be an integer.");
    }

    [Theory]
    [InlineData("DelayedInitialDelay", "soon")]
    [InlineData("DelayedMaxDelay", "")]
    [InlineData("DelayedMaxDelay", null)]
    public void should_report_a_duration_error_when_a_delay_does_not_parse(string key, string? value)
    {
        // given
        List<string> errors = [];
        var path = $"{_Section}:{key}";

        // when
        var parsed = FailurePolicyOverrides.TryParse([(key, path, value)], errors, out var overrides);

        // then
        parsed.Should().BeFalse();
        overrides.Should().BeNull();
        errors.Should().Equal($"Configuration '{path}' must be a duration such as '00:00:30'.");
    }

    [Fact]
    public void should_report_unknown_setting_when_key_is_not_a_failure_policy_setting()
    {
        // given
        List<string> errors = [];
        var path = $"{_Section}:MaxAttempts";

        // when
        var parsed = FailurePolicyOverrides.TryParse([("MaxAttempts", path, "5")], errors, out var overrides);

        // then
        parsed.Should().BeFalse();
        overrides.Should().BeNull();
        errors
            .Should()
            .Equal(
                $"Configuration '{path}' is not a failure policy setting. The supported settings are "
                    + "ImmediateRetries, DelayedRetries, DelayedInitialDelay, and DelayedMaxDelay."
            );
    }

    [Fact]
    public void should_report_every_invalid_setting_when_several_fail()
    {
        // given
        List<string> errors = ["existing"];
        (string Key, string Path, string? Value)[] settings =
        [
            ("ImmediateRetries", "p:ImmediateRetries", "x"),
            ("DelayedRetries", "p:DelayedRetries", "2"),
            ("DelayedInitialDelay", "p:DelayedInitialDelay", "y"),
            ("Unknown", "p:Unknown", "z"),
        ];

        // when
        var parsed = FailurePolicyOverrides.TryParse(settings, errors, out var overrides);

        // then
        parsed.Should().BeFalse();
        overrides.Should().BeNull();
        errors.Should().HaveCount(4);
        errors[0].Should().Be("existing");
        errors[1].Should().Be("Configuration 'p:ImmediateRetries' must be an integer.");
        errors[2].Should().Be("Configuration 'p:DelayedInitialDelay' must be a duration such as '00:00:30'.");
        errors[3].Should().StartWith("Configuration 'p:Unknown' is not a failure policy setting.");
    }
}
