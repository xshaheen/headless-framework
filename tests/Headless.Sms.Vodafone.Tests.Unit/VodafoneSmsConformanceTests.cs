// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Vodafone;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

/// <summary>
/// Runs the cross-provider <see cref="SmsSenderConformanceTests"/> contract against the Vodafone sender.
/// Vodafone-specific behavior (XML body, HMAC signature, body-based success detection, surfaced failure
/// body) lives in <see cref="VodafoneSmsSenderTests"/>.
/// </summary>
public sealed class VodafoneSmsConformanceTests : SmsSenderConformanceTests, IClassFixture<SmsWireMockFixture>
{
    private readonly SmsWireMockFixture _fixture;

    public VodafoneSmsConformanceTests(SmsWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    protected override ISmsSender CreateSuccessfulSender()
    {
        _fixture
            .Server.Given(Request.Create().WithPath("/submit").UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithBody("<Response><Success>true</Success></Response>")
            );

        return _CreateSender($"{_fixture.BaseUrl}/submit");
    }

    protected override ISmsSender CreateFaultingSender() => _CreateSender("http://localhost:1/submit");

    private VodafoneSmsSender _CreateSender(string endpoint)
    {
        var options = Options.Create(
            new VodafoneSmsOptions
            {
                SendSmsEndpoint = endpoint,
                Sender = "SENDER",
                AccountId = "acc",
                Password = "pass",
                SecureHash = "0123456789ABCDEF",
            }
        );

        return new VodafoneSmsSender(_fixture.HttpClientFactory, options, NullLogger<VodafoneSmsSender>.Instance);
    }

    [Fact]
    public override Task should_reject_a_null_request() => base.should_reject_a_null_request();

    [Fact]
    public override Task should_reject_a_null_destination() => base.should_reject_a_null_destination();

    [Fact]
    public override Task should_reject_a_request_with_an_empty_body() =>
        base.should_reject_a_request_with_an_empty_body();

    [Fact]
    public override Task should_succeed_for_a_single_destination() => base.should_succeed_for_a_single_destination();

    [Fact]
    public override Task should_report_a_transient_failure_on_a_transport_fault() =>
        base.should_report_a_transient_failure_on_a_transport_fault();

    [Fact]
    public override Task should_propagate_cancellation() => base.should_propagate_cancellation();
}
