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

    protected override ISmsSender CreateFaultingSender()
    {
        return _CreateSender("http://localhost:1/send");
    }

    private VictoryLinkSmsSender _CreateSender(string endpoint)
    {
        var options = new OptionsMonitorWrapper<VictoryLinkSmsOptions>(
            new VictoryLinkSmsOptions
            {
                Endpoint = endpoint,
                Sender = "SENDER",
                UserName = "user",
                Password = "pass",
            }
        );

        return new VictoryLinkSmsSender(
            _fixture.HttpClientFactory,
            SetupVictoryLink.HttpClientName,
            options,
            optionsName: null,
            NullLogger<VictoryLinkSmsSender>.Instance
        );
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
