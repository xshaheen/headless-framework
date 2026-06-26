// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Cequens;
using Headless.Sms.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

/// <summary>
/// Runs the cross-provider <see cref="SmsSenderConformanceTests"/> contract against the Cequens sender.
/// Cequens-specific behavior (token acquisition, caching, 401 re-auth, static-token fallback) lives in
/// <see cref="CequensSmsSenderTests"/>.
/// </summary>
public sealed class CequensSmsConformanceTests : SmsSenderConformanceTests, IClassFixture<SmsWireMockFixture>
{
    private readonly SmsWireMockFixture _fixture;

    public CequensSmsConformanceTests(SmsWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    protected override ISmsSender CreateSuccessfulSender()
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

    protected override ISmsSender CreateFaultingSender()
    {
        // Closed endpoints plus a static token: the token fetch fails fast and falls back to the static token,
        // so it is the send itself that faults at the transport layer (→ Transient), matching the contract.
        return _CreateSender("http://localhost:1", staticToken: "static-token");
    }

    private CequensSmsSender _CreateSender(string baseUrl, string? staticToken)
    {
        var options = Options.Create(
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
            new FakeTimeProvider(),
            options,
            NullLogger<CequensSmsSender>.Instance
        );
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
    public override Task should_succeed_for_a_single_destination() => base.should_succeed_for_a_single_destination();

    [Fact]
    public override Task should_report_a_transient_failure_on_a_transport_fault() =>
        base.should_report_a_transient_failure_on_a_transport_fault();

    [Fact]
    public override Task should_propagate_cancellation() => base.should_propagate_cancellation();
}
