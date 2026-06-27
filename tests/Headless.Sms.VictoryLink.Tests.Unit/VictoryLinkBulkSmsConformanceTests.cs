// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Testing;
using Headless.Sms.VictoryLink;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

/// <summary>Runs the <see cref="SmsBulkSenderConformanceTests"/> contract against the VictoryLink bulk sender.</summary>
public sealed class VictoryLinkBulkSmsConformanceTests
    : SmsBulkSenderConformanceTests,
        IClassFixture<SmsWireMockFixture>
{
    private readonly SmsWireMockFixture _fixture;

    public VictoryLinkBulkSmsConformanceTests(SmsWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    protected override IBulkSmsSender CreateSuccessfulSender()
    {
        _fixture
            .Server.Given(Request.Create().WithPath("/send").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBody("0"));

        return _CreateSender($"{_fixture.BaseUrl}/send");
    }

    protected override IBulkSmsSender CreateFaultingSender() => _CreateSender("http://localhost:1/send");

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
    public override Task should_reject_a_request_without_destinations() =>
        base.should_reject_a_request_without_destinations();

    [Fact]
    public override Task should_reject_a_request_with_an_empty_body() =>
        base.should_reject_a_request_with_an_empty_body();

    [Fact]
    public override Task should_return_a_result_for_every_recipient() =>
        base.should_return_a_result_for_every_recipient();

    [Fact]
    public override Task should_report_a_transient_failure_on_a_transport_fault() =>
        base.should_report_a_transient_failure_on_a_transport_fault();

    [Fact]
    public override Task should_propagate_cancellation() => base.should_propagate_cancellation();
}
