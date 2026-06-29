// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Twilio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;
using Twilio.Clients;
using Twilio.Http;

namespace Tests;

public sealed class TwilioSmsSenderTests
{
    private static TwilioSmsSender _CreateSender(ITwilioRestClient client)
    {
        var options = Options.Create(
            new TwilioSmsOptions
            {
                Sid = "AC0000000000000000000000000000000",
                AuthToken = "token",
                PhoneNumber = "+15551234567",
            }
        );

        return new TwilioSmsSender(client, options, NullLogger<TwilioSmsSender>.Instance);
    }

    [Fact]
    public async Task should_succeed_and_carry_the_message_sid()
    {
        var client = Substitute.For<ITwilioRestClient>();
        var response = new Response(HttpStatusCode.Created, """{"sid":"SM123","status":"queued"}""");
        client.RequestAsync(Arg.Any<Request>()).Returns(response);

        var result = await _CreateSender(client).SendAsync(SmsRequests.Single());

        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("SM123");
    }

    [Fact]
    public async Task should_reject_multiple_destinations_without_calling_twilio()
    {
        var client = Substitute.For<ITwilioRestClient>();

        var result = await _CreateSender(client).SendAsync(SmsRequests.Batch("hi", (20, "1"), (20, "2")));

        result.Success.Should().BeFalse();
        await client.DidNotReceive().RequestAsync(Arg.Any<Request>());
    }

    [Fact]
    public async Task should_return_transient_failure_when_the_sdk_throws()
    {
        var client = Substitute.For<ITwilioRestClient>();
        client.RequestAsync(Arg.Any<Request>()).ThrowsAsync(new HttpRequestException("network down"));

        var result = await _CreateSender(client).SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.Transient);
    }
}
