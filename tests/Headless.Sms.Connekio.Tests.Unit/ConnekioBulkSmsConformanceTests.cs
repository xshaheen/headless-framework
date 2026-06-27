// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Connekio;
using Headless.Sms.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

    protected override IBulkSmsSender CreateFaultingSender() => _CreateSender("http://localhost:1");

    private ConnekioSmsSender _CreateSender(string baseUrl)
    {
        var options = Options.Create(
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

        return new ConnekioSmsSender(_fixture.HttpClientFactory, options, NullLogger<ConnekioSmsSender>.Instance);
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
