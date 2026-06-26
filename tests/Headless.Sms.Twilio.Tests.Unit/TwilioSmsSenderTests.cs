// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Testing;
using Headless.Sms.Twilio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Twilio.Clients;
using Twilio.Http;

namespace Tests;

public sealed class TwilioSmsSenderTests
{
    private static TwilioSmsSender CreateSender(ITwilioRestClient client)
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

        var result = await CreateSender(client).SendAsync(SmsRequests.Single());

        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("SM123");
    }

    [Fact]
    public async Task should_reject_multiple_destinations_as_unsupported_without_calling_twilio()
    {
        var client = Substitute.For<ITwilioRestClient>();

        var result = await CreateSender(client).SendAsync(SmsRequests.Batch("hi", (20, "1"), (20, "2")));

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.Unsupported);
        await client.DidNotReceive().RequestAsync(Arg.Any<Request>());
    }
}
