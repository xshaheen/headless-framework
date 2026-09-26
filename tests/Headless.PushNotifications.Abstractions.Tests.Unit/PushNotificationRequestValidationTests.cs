// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;

namespace Tests;

public sealed class PushNotificationRequestValidationTests
{
    private static readonly Dictionary<string, string> _Data = new(StringComparer.Ordinal) { ["sync"] = "1" };

    [Fact]
    public void should_accept_a_notification_with_title_and_body()
    {
        // given
        var request = new PushNotificationRequest
        {
            Title = "title",
            Body = "body",
            Badge = 0,
            Sound = "default",
            Priority = PushNotificationPriority.Normal,
            TimeToLive = TimeSpan.Zero,
        };

        // when
        var action = () => PushNotificationRequestValidation.Validate(request);

        // then
        action.Should().NotThrow();
        PushNotificationRequestValidation.IsDataOnly(request).Should().BeFalse();
    }

    [Fact]
    public void should_accept_a_data_only_request()
    {
        // given
        var request = new PushNotificationRequest { Data = _Data, TimeToLive = TimeSpan.FromHours(1) };

        // when
        var action = () => PushNotificationRequestValidation.Validate(request);

        // then
        action.Should().NotThrow();
        PushNotificationRequestValidation.IsDataOnly(request).Should().BeTrue();
    }

    [Theory]
    [InlineData("title_without_body")]
    [InlineData("body_without_title")]
    [InlineData("blank_title")]
    [InlineData("blank_body")]
    [InlineData("no_title_body_or_data")]
    [InlineData("empty_data_only")]
    [InlineData("data_only_with_badge")]
    [InlineData("data_only_with_sound")]
    [InlineData("negative_badge")]
    [InlineData("negative_time_to_live")]
    [InlineData("blank_sound")]
    [InlineData("undefined_priority")]
    public void should_reject_an_invalid_request(string scenario)
    {
        // given
        var request = _Invalid(scenario);

        // when
        var action = () => PushNotificationRequestValidation.Validate(request);

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_accept_a_time_to_live_of_exactly_28_days()
    {
        // given
        var request = new PushNotificationRequest
        {
            Title = "title",
            Body = "body",
            TimeToLive = TimeSpan.FromDays(28),
        };

        // when
        var action = () => PushNotificationRequestValidation.Validate(request);

        // then
        action.Should().NotThrow();
    }

    [Fact]
    public void should_reject_a_time_to_live_over_28_days_with_the_limit_in_the_message()
    {
        // given
        var request = new PushNotificationRequest
        {
            Title = "title",
            Body = "body",
            TimeToLive = TimeSpan.FromDays(28) + TimeSpan.FromTicks(1),
        };

        // when
        var action = () => PushNotificationRequestValidation.Validate(request);

        // then
        action.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*28 days*");
    }

    [Fact]
    public void should_reject_the_largest_time_to_live_without_overflowing()
    {
        // given
        var request = new PushNotificationRequest { Data = _Data, TimeToLive = TimeSpan.MaxValue };

        // when
        var action = () => PushNotificationRequestValidation.Validate(request);

        // then
        action.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*28 days*");
    }

    [Fact]
    public void should_reject_a_null_request()
    {
        // when
        var action = () => PushNotificationRequestValidation.Validate(null!);

        // then
        action.Should().Throw<ArgumentNullException>();
    }

    private static PushNotificationRequest _Invalid(string scenario)
    {
        var notification = new PushNotificationRequest { Title = "title", Body = "body" };
        var dataOnly = new PushNotificationRequest { Data = _Data };

        return scenario switch
        {
            "title_without_body" => new PushNotificationRequest { Title = "title" },
            "body_without_title" => new PushNotificationRequest { Body = "body" },
            "blank_title" => notification with { Title = "   " },
            "blank_body" => notification with { Body = "   " },
            "no_title_body_or_data" => new PushNotificationRequest(),
            "empty_data_only" => new PushNotificationRequest
            {
                Data = new Dictionary<string, string>(StringComparer.Ordinal),
            },
            "data_only_with_badge" => dataOnly with { Badge = 1 },
            "data_only_with_sound" => dataOnly with { Sound = "default" },
            "negative_badge" => notification with { Badge = -1 },
            "negative_time_to_live" => notification with { TimeToLive = TimeSpan.FromSeconds(-1) },
            "blank_sound" => notification with { Sound = " " },
            "undefined_priority" => notification with { Priority = (PushNotificationPriority)42 },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };
    }
}
