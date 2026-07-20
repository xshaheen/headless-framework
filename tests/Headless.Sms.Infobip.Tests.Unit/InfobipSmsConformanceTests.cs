// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Hosting.Options;
using Headless.Sms;
using Headless.Sms.Infobip;
using Microsoft.Extensions.Logging.Abstractions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

/// <summary>
/// Runs the cross-provider <see cref="SmsSenderConformanceTests"/> contract against the Infobip sender.
/// Infobip-specific behavior (bulk id, per-message ids, API error mapping) lives in
/// <see cref="InfobipSmsSenderTests"/>.
/// </summary>
public sealed class InfobipSmsConformanceTests : SmsSenderConformanceTests, IClassFixture<SmsWireMockFixture>
{
    private const string _SuccessBody = """
        {
          "bulkId": "bulk-1",
          "messages": [
            {
              "to": "201001234567",
              "status": { "groupId": 1, "groupName": "PENDING", "id": 7, "name": "PENDING_ENROUTE", "description": "queued" },
              "messageId": "m1"
            }
          ]
        }
        """;

    private readonly SmsWireMockFixture _fixture;

    public InfobipSmsConformanceTests(SmsWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    protected override ISmsSender CreateSuccessfulSender()
    {
        _fixture
            .Server.Given(Request.Create().UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(_SuccessBody)
            );

        return _CreateSender(_fixture.BaseUrl);
    }

    protected override ISmsSender CreateFaultingSender()
    {
        return _CreateSender("http://localhost:1");
    }

    private InfobipSmsSender _CreateSender(string basePath)
    {
        var options = new OptionsMonitorWrapper<InfobipSmsOptions>(
            new InfobipSmsOptions
            {
                Sender = "SENDER",
                ApiKey = "api-key",
                BasePath = basePath,
            }
        );

        return new InfobipSmsSender(
            _fixture.HttpClientFactory,
            SetupInfobip.HttpClientName,
            options,
            optionsName: null,
            NullLogger<InfobipSmsSender>.Instance
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
