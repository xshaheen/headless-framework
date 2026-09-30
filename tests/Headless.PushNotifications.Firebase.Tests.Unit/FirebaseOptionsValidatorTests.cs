// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Headless.PushNotifications.Firebase;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

    [Fact]
    public void should_default_to_two_retries_capped_at_five_minutes_and_sender_id_mismatch_as_failure()
    {
        // when
        var options = new FirebaseOptions { Json = "{}" };

        // then
        options.Retry.MaxAttempts.Should().Be(2);
        options.Retry.MaxDelay.Should().Be(TimeSpan.FromMinutes(5));
        options.TreatSenderIdMismatchAsUnregistered.Should().BeFalse();
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
    [InlineData(5, true)]
    [InlineData(6, false)]
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

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(3600, true)]
    [InlineData(3601, false)]
    public void should_validate_max_delay_range(int seconds, bool expectedValid)
    {
        // when
        var result = _Validator.Validate(
            new FirebaseOptions
            {
                Json = "{}",
                Retry = new FirebaseRetryOptions { MaxDelay = TimeSpan.FromSeconds(seconds) },
            }
        );

        // then
        result.IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void should_copy_every_setting_from_a_prebuilt_options_instance()
    {
        // given
        var source = new FirebaseOptions
        {
            Json = "{}",
            TreatSenderIdMismatchAsUnregistered = true,
            Retry = new FirebaseRetryOptions { MaxAttempts = 4, MaxDelay = TimeSpan.FromMinutes(2) },
        };
        var services = new ServiceCollection();

        // when
        SetupFirebasePushNotifications.CopyOptions(source)(services, null);
        using var provider = services.BuildServiceProvider();
        var copy = provider.GetRequiredService<IOptions<FirebaseOptions>>().Value;

        // then
        copy.Json.Should().Be("{}");
        copy.TreatSenderIdMismatchAsUnregistered.Should().BeTrue();
        copy.Retry.MaxAttempts.Should().Be(4);
        copy.Retry.MaxDelay.Should().Be(TimeSpan.FromMinutes(2));
    }
}
