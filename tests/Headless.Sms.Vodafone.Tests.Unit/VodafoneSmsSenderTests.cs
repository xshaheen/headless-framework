// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Vodafone;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed class VodafoneSmsSenderTests : TestBase, IClassFixture<SmsWireMockFixture>
{
    private readonly SmsWireMockFixture _fixture;

    public VodafoneSmsSenderTests(SmsWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private VodafoneSmsSender _CreateSender()
    {
        var options = Options.Create(
            new VodafoneSmsOptions
            {
                SendSmsEndpoint = $"{_fixture.BaseUrl}/submit",
                Sender = "SENDER",
                AccountId = "acc",
                Password = "pass",
                SecureHash = "0123456789ABCDEF",
            }
        );

        return new VodafoneSmsSender(_fixture.HttpClientFactory, options, NullLogger<VodafoneSmsSender>.Instance);
    }

    private void _StubSubmit(string body)
    {
        _fixture
            .Server.Given(Request.Create().WithPath("/submit").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBody(body));
    }

    [Fact]
    public async Task should_succeed_when_response_reports_success()
    {
        _StubSubmit("<Response><Success>true</Success></Response>");

        var result = await _CreateSender().SendAsync(SmsRequests.Single(), AbortToken);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_succeed_for_a_case_insensitive_success_marker()
    {
        _StubSubmit("<RESPONSE><SUCCESS>TRUE</SUCCESS></RESPONSE>");

        var result = await _CreateSender().SendAsync(SmsRequests.Single(), AbortToken);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_and_surface_the_response_body_when_it_does_not_report_success()
    {
        const string body = "<Response><Success>false</Success><Error>blocked</Error></Response>";
        _StubSubmit(body);

        var result = await _CreateSender().SendAsync(SmsRequests.Single(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureError.Should().Be(body);
    }

    [Fact]
    public async Task should_fail_on_empty_body()
    {
        _StubSubmit(string.Empty);

        var result = await _CreateSender().SendAsync(SmsRequests.Single(), AbortToken);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task should_xml_escape_message_content()
    {
        _StubSubmit("<Response><Success>true</Success></Response>");

        await _CreateSender().SendAsync(SmsRequests.Single(text: "a & b < c"), AbortToken);

        var body = _fixture.Server.LogEntries.Single().RequestMessage?.Body;
        body.Should().Contain("a &amp; b &lt; c");
    }

    [Theory]
    [InlineData(nameof(TimeoutRejectedException))]
    [InlineData(nameof(BrokenCircuitException))]
    [InlineData(nameof(RateLimiterRejectedException))]
    public async Task should_classify_resilience_rejections_as_transient(string rejectionKind)
    {
        var exception = ResilienceRejections.Create(rejectionKind);
        var options = Options.Create(
            new VodafoneSmsOptions
            {
                SendSmsEndpoint = "http://localhost:1/submit",
                Sender = "SENDER",
                AccountId = "acc",
                Password = "pass",
                SecureHash = "0123456789ABCDEF",
            }
        );
        var sender = new VodafoneSmsSender(
            new ThrowingHttpClientFactory(exception),
            options,
            NullLogger<VodafoneSmsSender>.Instance
        );

        var result = await sender.SendAsync(SmsRequests.Single(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.Transient);
    }
}
