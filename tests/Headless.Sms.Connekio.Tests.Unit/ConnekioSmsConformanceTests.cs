// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Connekio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

/// <summary>
/// Runs the cross-provider <see cref="SmsSenderConformanceTests"/> contract against the Connekio sender.
/// Connekio-specific behavior (batch routing, Basic auth header, surfaced failure body) lives in
/// <see cref="ConnekioSmsSenderTests"/>.
/// </summary>
public sealed class ConnekioSmsConformanceTests : SmsSenderConformanceTests, IClassFixture<SmsWireMockFixture>
{
    private readonly SmsWireMockFixture _fixture;

    public ConnekioSmsConformanceTests(SmsWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    protected override ISmsSender CreateSuccessfulSender()
    {
        _fixture
            .Server.Given(Request.Create().WithPath("/single").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBody("{}"));

        return _CreateSender(_fixture.BaseUrl);
    }

    protected override ISmsSender CreateFaultingSender()
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
