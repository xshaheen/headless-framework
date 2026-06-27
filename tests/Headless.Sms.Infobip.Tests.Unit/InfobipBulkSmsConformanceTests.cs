// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Infobip;
using Headless.Sms.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

/// <summary>Runs the <see cref="SmsBulkSenderConformanceTests"/> contract against the Infobip bulk sender.</summary>
public sealed class InfobipBulkSmsConformanceTests : SmsBulkSenderConformanceTests, IClassFixture<SmsWireMockFixture>
{
    // Two messages so the per-recipient mapping matches the two-recipient conformance request.
    private const string _SuccessBody = """
        {
          "bulkId": "bulk-1",
          "messages": [
            { "to": "201001234567", "status": { "groupId": 1, "groupName": "PENDING", "id": 7, "name": "PENDING_ENROUTE", "description": "queued" }, "messageId": "m1" },
            { "to": "201009876543", "status": { "groupId": 1, "groupName": "PENDING", "id": 7, "name": "PENDING_ENROUTE", "description": "queued" }, "messageId": "m2" }
          ]
        }
        """;

    private readonly SmsWireMockFixture _fixture;

    public InfobipBulkSmsConformanceTests(SmsWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    protected override IBulkSmsSender CreateSuccessfulSender()
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

    protected override IBulkSmsSender CreateFaultingSender() => _CreateSender("http://localhost:1");

    private InfobipSmsSender _CreateSender(string basePath)
    {
        var options = Options.Create(
            new InfobipSmsOptions
            {
                Sender = "SENDER",
                ApiKey = "api-key",
                BasePath = basePath,
            }
        );

        return new InfobipSmsSender(_fixture.HttpClientFactory, options, NullLogger<InfobipSmsSender>.Instance);
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
