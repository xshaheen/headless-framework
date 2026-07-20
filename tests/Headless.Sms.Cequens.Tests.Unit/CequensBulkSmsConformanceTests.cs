// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Hosting.Options;
using Headless.Sms;
using Headless.Sms.Cequens;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

/// <summary>Runs the <see cref="SmsBulkSenderConformanceTests"/> contract against the Cequens bulk sender.</summary>
public sealed class CequensBulkSmsConformanceTests : SmsBulkSenderConformanceTests, IClassFixture<SmsWireMockFixture>
{
    private readonly SmsWireMockFixture _fixture;

    public CequensBulkSmsConformanceTests(SmsWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    protected override IBulkSmsSender CreateSuccessfulSender()
    {
        _fixture
            .Server.Given(Request.Create().WithPath("/auth").UsingPost())
            .RespondWith(
                Response.Create().WithStatusCode(HttpStatusCode.OK).WithBody("{\"data\":{\"access_token\":\"tok\"}}")
            );
        _fixture
            .Server.Given(Request.Create().WithPath("/sms").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBody("{}"));

        return _CreateSender(_fixture.BaseUrl, staticToken: null);
    }

    protected override IBulkSmsSender CreateFaultingSender()
    {
        return _CreateSender("http://localhost:1", staticToken: "static-token");
    }

    private CequensSmsSender _CreateSender(string baseUrl, string? staticToken)
    {
        var options = new OptionsMonitorWrapper<CequensSmsOptions>(
            new CequensSmsOptions
            {
                SingleSmsEndpoint = $"{baseUrl}/sms",
                TokenEndpoint = $"{baseUrl}/auth",
                ApiKey = "api-key",
                UserName = "user",
                SenderName = "SENDER",
                Token = staticToken,
            }
        );

        return new CequensSmsSender(
            _fixture.HttpClientFactory,
            SetupCequens.HttpClientName,
            new FakeTimeProvider(),
            options,
            optionsName: null,
            NullLogger<CequensSmsSender>.Instance
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
