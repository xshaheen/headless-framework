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
}
