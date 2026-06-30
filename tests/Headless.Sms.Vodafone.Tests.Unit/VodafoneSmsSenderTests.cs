// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Vodafone;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed class VodafoneSmsSenderTests : IClassFixture<SmsWireMockFixture>
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

        var result = await _CreateSender().SendAsync(SmsRequests.Single());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_succeed_for_a_case_insensitive_success_marker()
    {
        _StubSubmit("<RESPONSE><SUCCESS>TRUE</SUCCESS></RESPONSE>");

        var result = await _CreateSender().SendAsync(SmsRequests.Single());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_when_response_does_not_report_success()
    {
        _StubSubmit("<Response><Success>false</Success></Response>");

        var result = await _CreateSender().SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_on_empty_body()
    {
        _StubSubmit(string.Empty);

        var result = await _CreateSender().SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task should_xml_escape_message_content()
    {
        _StubSubmit("<Response><Success>true</Success></Response>");

        await _CreateSender().SendAsync(SmsRequests.Single(text: "a & b < c"));

        var body = _fixture.Server.LogEntries.Single().RequestMessage?.Body;
        body.Should().Contain("a &amp; b &lt; c");
    }

    [Fact]
    public async Task should_return_transient_failure_on_transport_fault()
    {
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
        var sender = new VodafoneSmsSender(_fixture.HttpClientFactory, options, NullLogger<VodafoneSmsSender>.Instance);

        var result = await sender.SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.Transient);
    }
}
