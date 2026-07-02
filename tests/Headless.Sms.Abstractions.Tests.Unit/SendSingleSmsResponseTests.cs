// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;

namespace Tests;

public sealed class SendSingleSmsResponseTests
{
    [Fact]
    public void should_report_success_without_a_provider_message_id()
    {
        // when
        var response = SendSingleSmsResponse.Succeeded();

        // then
        response.Success.Should().BeTrue();
        response.ProviderMessageId.Should().BeNull();
        response.FailureError.Should().BeNull();
        response.FailureKind.Should().Be(SmsFailureKind.None);
    }

    [Fact]
    public void should_carry_the_provider_message_id_on_success()
    {
        // when
        var response = SendSingleSmsResponse.Succeeded("SM-123");

        // then
        response.Success.Should().BeTrue();
        response.ProviderMessageId.Should().Be("SM-123");
    }

    [Fact]
    public void should_report_failure_with_default_unknown_kind()
    {
        // when
        var response = SendSingleSmsResponse.Failed("boom");

        // then
        response.Success.Should().BeFalse();
        response.FailureError.Should().Be("boom");
        response.FailureKind.Should().Be(SmsFailureKind.Unknown);
    }

    [Fact]
    public void should_carry_the_explicit_failure_kind()
    {
        // when
        var response = SendSingleSmsResponse.Failed("rate", SmsFailureKind.RateLimited);

        // then
        response.Success.Should().BeFalse();
        response.FailureKind.Should().Be(SmsFailureKind.RateLimited);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void should_reject_null_or_empty_failure_error(string? failureError)
    {
        // when
        var act = () => SendSingleSmsResponse.Failed(failureError!);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_build_a_classified_failure_from_a_transport_exception()
    {
        // when
        var response = SendSingleSmsResponse.FromException(new HttpRequestException("boom"));

        // then
        response.Success.Should().BeFalse();
        response.FailureError.Should().Be("boom");
        response.FailureKind.Should().Be(SmsFailureKind.Transient);
    }

    [Fact]
    public void should_fall_back_to_the_exception_type_name_when_the_message_is_empty()
    {
        // when
        var response = SendSingleSmsResponse.FromException(new InvalidOperationException(""));

        // then
        response.Success.Should().BeFalse();
        response.FailureError.Should().Be(nameof(InvalidOperationException));
        response.FailureKind.Should().Be(SmsFailureKind.Unknown);
    }

    [Fact]
    public void should_build_a_failure_with_an_explicitly_classified_kind()
    {
        // when
        var response = SendSingleSmsResponse.FromException(
            new InvalidOperationException("throttled"),
            SmsFailureKind.RateLimited
        );

        // then
        response.Success.Should().BeFalse();
        response.FailureError.Should().Be("throttled");
        response.FailureKind.Should().Be(SmsFailureKind.RateLimited);
    }

    [Fact]
    public void should_reject_a_null_exception()
    {
        // when
        var act = () => SendSingleSmsResponse.FromException(null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_reject_a_null_exception_with_an_explicit_kind()
    {
        // when
        var act = () => SendSingleSmsResponse.FromException(null!, SmsFailureKind.Transient);

        // then
        act.Should().Throw<ArgumentNullException>();
    }
}
