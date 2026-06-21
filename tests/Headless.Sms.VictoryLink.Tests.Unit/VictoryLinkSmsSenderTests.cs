// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Testing;
using Headless.Sms.VictoryLink;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class VictoryLinkSmsSenderTests
{
    private static VictoryLinkSmsSender CreateSender(TestHttpMessageHandler handler)
    {
        var options = Options.Create(
            new VictoryLinkSmsOptions
            {
                Endpoint = "https://victorylink.test/send",
                Sender = "SENDER",
                UserName = "user",
                Password = "pass",
            }
        );

        return new VictoryLinkSmsSender(
            new TestHttpClientFactory(handler),
            options,
            NullLogger<VictoryLinkSmsSender>.Instance
        );
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    [InlineData("0\r\n")] // trailing whitespace from the transport
    [InlineData("\"0\"")] // JSON-quoted body
    public async Task should_succeed_for_success_codes_with_noisy_bodies(string body)
    {
        var handler = TestHttpMessageHandler.Respond(HttpStatusCode.OK, body);
        var sender = CreateSender(handler);

        var result = await sender.SendAsync(SmsRequests.Single());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_and_map_out_of_credit()
    {
        var handler = TestHttpMessageHandler.Respond(HttpStatusCode.OK, "-5");
        var sender = CreateSender(handler);

        var result = await sender.SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.OutOfCredit);
    }

    [Fact]
    public async Task should_fail_on_empty_body()
    {
        var handler = TestHttpMessageHandler.Respond(HttpStatusCode.OK, "");
        var sender = CreateSender(handler);

        var result = await sender.SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_transient_failure_on_transport_fault()
    {
        var handler = TestHttpMessageHandler.Throws(new HttpRequestException("connection reset"));
        var sender = CreateSender(handler);

        var result = await sender.SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.Transient);
    }

    [Fact]
    public async Task should_propagate_cancellation()
    {
        var handler = TestHttpMessageHandler.Respond(HttpStatusCode.OK, "0");
        var sender = CreateSender(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await sender.SendAsync(SmsRequests.Single(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_include_the_country_code_in_the_recipient()
    {
        var handler = TestHttpMessageHandler.Respond(HttpStatusCode.OK, "0");
        var sender = CreateSender(handler);

        await sender.SendAsync(SmsRequests.Single(code: 20, number: "1001234567"));

        handler.RequestBodies[0].Should().Contain("201001234567");
    }
}
