// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Testing;
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

    private VodafoneSmsSender CreateSender()
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

    private void StubSubmit(string body)
    {
        _fixture
            .Server.Given(Request.Create().WithPath("/submit").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBody(body));
    }

    [Fact]
    public async Task should_succeed_when_response_reports_success()
    {
        StubSubmit("<Response><Success>true</Success></Response>");

        var result = await CreateSender().SendAsync(SmsRequests.Single());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_succeed_for_a_case_insensitive_success_marker()
    {
        StubSubmit("<RESPONSE><SUCCESS>TRUE</SUCCESS></RESPONSE>");

        var result = await CreateSender().SendAsync(SmsRequests.Single());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_and_surface_the_response_body_when_it_does_not_report_success()
    {
        const string body = "<Response><Success>false</Success><Error>blocked</Error></Response>";
        StubSubmit(body);

        var result = await CreateSender().SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
        result.FailureError.Should().Be(body);
    }

    [Fact]
    public async Task should_fail_on_empty_body()
    {
        StubSubmit(string.Empty);

        var result = await CreateSender().SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task should_xml_escape_message_content()
    {
        StubSubmit("<Response><Success>true</Success></Response>");

        await CreateSender().SendAsync(SmsRequests.Single(text: "a & b < c"));

        var body = _fixture.Server.LogEntries.Single().RequestMessage?.Body;
        body.Should().Contain("a &amp; b &lt; c");
    }
}
