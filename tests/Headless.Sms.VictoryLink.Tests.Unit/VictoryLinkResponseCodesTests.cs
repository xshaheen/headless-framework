// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.VictoryLink.Internals;

namespace Tests;

public sealed class VictoryLinkResponseCodesTests
{
    [Theory]
    [InlineData("0", true)]
    [InlineData("-10", true)]
    [InlineData("-1", false)]
    [InlineData("-5", false)]
    [InlineData("-100", false)]
    public void should_classify_success_codes(string code, bool expected)
    {
        VictoryLinkResponseCodes.IsSuccess(code).Should().Be(expected);
    }

    [Fact]
    public void should_describe_known_codes()
    {
        VictoryLinkResponseCodes.GetCodeMeaning("0").Should().Be("Message Sent Successfully");
        VictoryLinkResponseCodes.GetCodeMeaning("-5").Should().Be("Out of credit.");
    }

    [Fact]
    public void should_describe_unknown_codes_with_the_raw_code()
    {
        VictoryLinkResponseCodes.GetCodeMeaning("999").Should().Contain("999");
    }

    [Theory]
    [InlineData("-5", SmsFailureKind.OutOfCredit)]
    [InlineData("-25", SmsFailureKind.RateLimited)]
    [InlineData("-1", SmsFailureKind.InvalidRecipient)]
    [InlineData("-100", SmsFailureKind.Unknown)]
    public void should_map_failure_kind(string code, SmsFailureKind expected)
    {
        VictoryLinkResponseCodes.GetFailureKind(code).Should().Be(expected);
    }
}
