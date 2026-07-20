// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Hosting.Options;
using Headless.Sms;
using Headless.Sms.Connekio;
using Microsoft.Extensions.Logging.Abstractions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

/// <summary>Runs the <see cref="SmsBulkSenderConformanceTests"/> contract against the Connekio bulk sender.</summary>
public sealed class ConnekioBulkSmsConformanceTests : SmsBulkSenderConformanceTests, IClassFixture<SmsWireMockFixture>
{
    private readonly SmsWireMockFixture _fixture;

    public ConnekioBulkSmsConformanceTests(SmsWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    protected override IBulkSmsSender CreateSuccessfulSender()
    {
        // Bulk sends route to the dedicated batch endpoint.
        _fixture
            .Server.Given(Request.Create().WithPath("/batch").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBody("{}"));

        return _CreateSender(_fixture.BaseUrl);
    }

    protected override IBulkSmsSender CreateFaultingSender()
    {
        return _CreateSender("http://localhost:1");
    }

    private ConnekioSmsSender _CreateSender(string baseUrl)
    {
        var options = new OptionsMonitorWrapper<ConnekioSmsOptions>(
            new ConnekioSmsOptions
            {
                SingleSmsEndpoint = $"{baseUrl}/single",
                BatchSmsEndpoint = $"{baseUrl}/batch",
                Sender = "SENDER",
                AccountId = "acc",
                UserName = "user",
                Password = "pass",
            }
        );

        return new ConnekioSmsSender(
            _fixture.HttpClientFactory,
            SetupConnekio.HttpClientName,
            options,
            optionsName: null,
            NullLogger<ConnekioSmsSender>.Instance
        );
    }

    [Fact]
    public override Task should_reject_a_null_request()
    {
        return base.should_reject_a_null_request();
    }

    [Fact]
    public override Task should_reject_a_request_without_destinations()
    {
        return base.should_reject_a_request_without_destinations();
    }

    [Fact]
    public override Task should_reject_null_destinations()
    {
        return base.should_reject_null_destinations();
    }

    [Fact]
    public override Task should_reject_a_request_with_an_empty_body()
    {
        return base.should_reject_a_request_with_an_empty_body();
    }

    [Fact]
    public override Task should_return_a_result_for_every_recipient()
    {
        return base.should_return_a_result_for_every_recipient();
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
