// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications.Firebase;

namespace Tests;

public sealed class FirebaseOptionsValidatorTests
{
    private static readonly FirebaseOptionsValidator _Validator = new();

    [Fact]
    public void should_be_valid_when_json_present_with_defaults()
    {
        // when
        var result = _Validator.Validate(new FirebaseOptions { Json = "{}" });

        // then
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void should_be_invalid_when_json_is_blank(string json)
    {
        // when
        var result = _Validator.Validate(new FirebaseOptions { Json = json });

        // then
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(-1, false)]
    public void should_validate_max_attempts_range(int maxAttempts, bool expectedValid)
    {
        // when
        var result = _Validator.Validate(
            new FirebaseOptions
            {
                Json = "{}",
                Retry = new FirebaseRetryOptions { MaxAttempts = maxAttempts },
            }
        );

        // then
        result.IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void should_be_invalid_when_max_delay_below_one_second()
    {
        // when
        var result = _Validator.Validate(
            new FirebaseOptions
            {
                Json = "{}",
                Retry = new FirebaseRetryOptions { MaxDelay = TimeSpan.Zero },
            }
        );

        // then
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_be_invalid_when_rate_limit_delay_above_five_minutes()
    {
        // when
        var result = _Validator.Validate(
            new FirebaseOptions
            {
                Json = "{}",
                Retry = new FirebaseRetryOptions { RateLimitDelay = TimeSpan.FromMinutes(6) },
            }
        );

        // then
        result.IsValid.Should().BeFalse();
    }
}
