// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.VictoryLink;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

/// <summary>
/// Runs the cross-provider <see cref="SmsSenderConformanceTests"/> contract against the VictoryLink sender.
/// VictoryLink-specific behavior (numeric response codes, RTL language detection, recipient formatting) lives
/// in <see cref="VictoryLinkSmsSenderTests"/>.
/// </summary>
public sealed class VictoryLinkSmsConformanceTests : SmsSenderConformanceTests, IClassFixture<SmsWireMockFixture>
{
    private readonly SmsWireMockFixture _fixture;

    public VictoryLinkSmsConformanceTests(SmsWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    protected override ISmsSender CreateSuccessfulSender()
    {
        _fixture
            .Server.Given(Request.Create().WithPath("/send").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBody("0"));

        return _CreateSender($"{_fixture.BaseUrl}/send");
    }

    protected override ISmsSender CreateFaultingSender() => _CreateSender("http://localhost:1/send");

    private VictoryLinkSmsSender _CreateSender(string endpoint)
    {
        var options = Options.Create(
            new VictoryLinkSmsOptions
            {
                Endpoint = endpoint,
                Sender = "SENDER",
                UserName = "user",
                Password = "pass",
            }
        );

        return new VictoryLinkSmsSender(_fixture.HttpClientFactory, options, NullLogger<VictoryLinkSmsSender>.Instance);
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
