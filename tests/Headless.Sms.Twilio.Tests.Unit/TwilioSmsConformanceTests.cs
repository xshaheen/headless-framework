// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Hosting.Options;
using Headless.Sms;
using Headless.Sms.Twilio;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;
using Twilio.Clients;
using Twilio.Http;

namespace Tests;

/// <summary>
/// Runs the cross-provider <see cref="SmsSenderConformanceTests"/> contract against the Twilio sender.
/// Twilio-specific behavior (message SID, multi-destination rejection) lives in
/// <see cref="TwilioSmsSenderTests"/>.
/// </summary>
public sealed class TwilioSmsConformanceTests : SmsSenderConformanceTests
{
    protected override ISmsSender CreateSuccessfulSender()
    {
        var client = Substitute.For<ITwilioRestClient>();
        client
            .RequestAsync(Arg.Any<Request>())
            .Returns(new Response(HttpStatusCode.Created, """{"sid":"SM123","status":"queued"}"""));

        return _CreateSender(client);
    }

    protected override ISmsSender CreateFaultingSender()
    {
        var client = Substitute.For<ITwilioRestClient>();
        client.RequestAsync(Arg.Any<Request>()).ThrowsAsync(new HttpRequestException("network down"));

        return _CreateSender(client);
    }

    private static TwilioSmsSender _CreateSender(ITwilioRestClient client)
    {
        var options = new OptionsMonitorWrapper<TwilioSmsOptions>(
            new TwilioSmsOptions
            {
                Sid = "AC0000000000000000000000000000000",
                AuthToken = "token",
                PhoneNumber = "+15551234567",
            }
        );

        return new TwilioSmsSender(client, options, optionsName: null, NullLogger<TwilioSmsSender>.Instance);
    }

    [Fact]
    public override Task should_reject_a_null_request()
    {
        return base.should_reject_a_null_request();
    }

    [Fact]
    public override Task should_reject_a_null_destination()
    {
        return base.should_reject_a_null_destination();
    }

    [Fact]
    public override Task should_reject_a_request_with_an_empty_body()
    {
        return base.should_reject_a_request_with_an_empty_body();
    }

    [Fact]
    public override Task should_succeed_for_a_single_destination()
    {
        return base.should_succeed_for_a_single_destination();
    }

    [Fact]
    public override Task should_report_a_transient_failure_on_a_transport_fault()
    {
        return base.should_report_a_transient_failure_on_a_transport_fault();
    }

    [Fact]
    public override Task should_propagate_cancellation()
    {
        return base.should_propagate_cancellation();
    }
}
