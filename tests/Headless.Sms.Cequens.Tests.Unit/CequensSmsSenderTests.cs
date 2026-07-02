// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Cequens;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Polly.Timeout;
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

    [Fact]
    public async Task should_classify_a_persistent_unauthorized_as_an_auth_failure()
    {
        var time = new FakeTimeProvider();
        _StubTokenOk("tok");
        _fixture
            .Server.Given(Request.Create().WithPath("/sms").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.Unauthorized).WithBody("unauthorized"));

        var result = await _CreateSender(time).SendAsync(SmsRequests.Single(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureError.Should().Be("unauthorized");
        result.FailureKind.Should().Be(SmsFailureKind.AuthFailure);
        _SendCalls().Should().Be(2); // the 401, then the re-authenticated retry that also got a 401
    }

    [Fact]
    public async Task should_surface_the_error_body_without_guessing_a_kind_on_a_server_error()
    {
        var time = new FakeTimeProvider();
        _StubTokenOk("tok");
        _fixture
            .Server.Given(Request.Create().WithPath("/sms").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError).WithBody("down"));

        var result = await _CreateSender(time).SendAsync(SmsRequests.Single(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureError.Should().Be("down");

        // Cequens documents no error contract, so a 5xx is not assumed to be retryable.
        result.FailureKind.Should().Be(SmsFailureKind.Unknown);
    }

    [Fact]
    public async Task should_classify_a_resilience_timeout_as_transient()
    {
        var options = Options.Create(
            new CequensSmsOptions
            {
                SingleSmsEndpoint = "http://localhost:1/sms",
                TokenEndpoint = "http://localhost:1/auth",
                ApiKey = "api-key",
                UserName = "user",
                SenderName = "SENDER",
                Token = "static-token", // the throwing token fetch falls back here, so the send itself faults
            }
        );
        using var sender = new CequensSmsSender(
            new ThrowingHttpClientFactory(new TimeoutRejectedException("pipeline timeout")),
            new FakeTimeProvider(),
            options,
            NullLogger<CequensSmsSender>.Instance
        );

        var result = await sender.SendAsync(SmsRequests.Single(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.Transient);
    }
}
