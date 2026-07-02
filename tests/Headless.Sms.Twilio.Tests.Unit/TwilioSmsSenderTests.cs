// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms.Twilio;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Twilio.Clients;
using Twilio.Http;

namespace Tests;

public sealed class TwilioSmsSenderTests : TestBase
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

        var result = await _CreateSender(client).SendAsync(SmsRequests.Single(), AbortToken);
        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("SM123");
    }
}
