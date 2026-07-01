// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Cequens;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed class CequensSmsSenderTests : TestBase, IClassFixture<SmsWireMockFixture>
{
    private readonly SmsWireMockFixture _fixture;

    public CequensSmsSenderTests(SmsWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private CequensSmsSender _CreateSender(FakeTimeProvider time, string? staticToken = null)
    {
        var options = Options.Create(
            new CequensSmsOptions
            {
                SingleSmsEndpoint = $"{_fixture.BaseUrl}/sms",
                TokenEndpoint = $"{_fixture.BaseUrl}/auth",
                ApiKey = "api-key",
                UserName = "user",
                SenderName = "SENDER",
                Token = staticToken,
            }
        );

        return new CequensSmsSender(_fixture.HttpClientFactory, time, options, NullLogger<CequensSmsSender>.Instance);
    }

    private void _StubTokenOk(string token)
    {
        _fixture
            .Server.Given(Request.Create().WithPath("/auth").UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithBody($"{{\"data\":{{\"access_token\":\"{token}\"}}}}")
            );
    }

    private void _StubTokenError()
    {
        _fixture
            .Server.Given(Request.Create().WithPath("/auth").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError).WithBody("down"));
    }

    private void _StubSendOk()
    {
        _fixture
            .Server.Given(Request.Create().WithPath("/sms").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBody("{}"));
    }

    private int _TokenCalls()
    {
        return _fixture.Server.FindLogEntries(Request.Create().WithPath("/auth").UsingPost()).Count;
    }

    private int _SendCalls()
    {
        return _fixture.Server.FindLogEntries(Request.Create().WithPath("/sms").UsingPost()).Count;
    }

    [Fact]
    public async Task should_send_successfully_and_cache_the_token_across_sends()
    {
        var time = new FakeTimeProvider();
        _StubTokenOk("tok-1");
        _StubSendOk();
        using var sender = _CreateSender(time);

        var first = await sender.SendAsync(SmsRequests.Single(), AbortToken);
        var second = await sender.SendAsync(SmsRequests.Single(), AbortToken);

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        _TokenCalls().Should().Be(1); // fetched once, reused for the second send
        _SendCalls().Should().Be(2);
    }

    [Fact]
    public async Task should_invalidate_the_token_and_retry_on_unauthorized()
    {
        var time = new FakeTimeProvider();
        _StubTokenOk("tok");

        const string scenario = "auth-retry";
        _fixture
            .Server.Given(Request.Create().WithPath("/sms").UsingPost())
            .InScenario(scenario)
            .WillSetStateTo("retried")
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.Unauthorized).WithBody("unauthorized"));
        _fixture
            .Server.Given(Request.Create().WithPath("/sms").UsingPost())
            .InScenario(scenario)
            .WhenStateIs("retried")
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBody("{}"));

        using var sender = _CreateSender(time);

        var result = await sender.SendAsync(SmsRequests.Single(), AbortToken);

        result.Success.Should().BeTrue();
        _TokenCalls().Should().Be(2); // initial fetch + re-auth after the 401
        _SendCalls().Should().Be(2); // 401, then the retry succeeds
    }

    [Fact]
    public async Task should_fall_back_to_the_static_token_when_sign_in_fails()
    {
        var time = new FakeTimeProvider();
        _StubTokenError();
        _StubSendOk();
        using var sender = _CreateSender(time, staticToken: "static-fallback");

        var result = await sender.SendAsync(SmsRequests.Single(), AbortToken);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_with_auth_failure_when_no_token_is_available()
    {
        var time = new FakeTimeProvider();
        _StubTokenError();
        using var sender = _CreateSender(time);

        var result = await sender.SendAsync(SmsRequests.Single(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.AuthFailure);
    }
}
