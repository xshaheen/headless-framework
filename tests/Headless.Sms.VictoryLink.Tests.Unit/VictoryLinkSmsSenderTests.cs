// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.VictoryLink;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed class VictoryLinkSmsSenderTests : TestBase, IClassFixture<SmsWireMockFixture>
{
    private readonly SmsWireMockFixture _fixture;

    public VictoryLinkSmsSenderTests(SmsWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private VictoryLinkSmsSender _CreateSender()
    {
        var options = Options.Create(
            new VictoryLinkSmsOptions
            {
                Endpoint = $"{_fixture.BaseUrl}/send",
                Sender = "SENDER",
                UserName = "user",
                Password = "pass",
            }
        );

        return new VictoryLinkSmsSender(_fixture.HttpClientFactory, options, NullLogger<VictoryLinkSmsSender>.Instance);
    }

    private void _StubSend(HttpStatusCode statusCode, string body)
    {
        _fixture
            .Server.Given(Request.Create().WithPath("/send").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(statusCode).WithBody(body));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    [InlineData("0\r\n")] // trailing whitespace from the transport
    [InlineData("\"0\"")] // JSON-quoted body
    public async Task should_succeed_for_success_codes_with_noisy_bodies(string body)
    {
        _StubSend(HttpStatusCode.OK, body);

        var result = await _CreateSender().SendAsync(SmsRequests.Single(), AbortToken);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_and_map_out_of_credit()
    {
        _StubSend(HttpStatusCode.OK, "-5");

        var result = await _CreateSender().SendAsync(SmsRequests.Single(), AbortToken);
        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.OutOfCredit);
    }

    [Fact]
    public async Task should_fail_on_empty_body()
    {
        _StubSend(HttpStatusCode.OK, string.Empty);

        var result = await _CreateSender().SendAsync(SmsRequests.Single(), AbortToken);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task should_include_the_country_code_in_the_recipient()
    {
        _StubSend(HttpStatusCode.OK, "0");

        await _CreateSender().SendAsync(SmsRequests.Single(code: 20, number: "1001234567"), AbortToken);
        var body = _fixture.Server.LogEntries.Single().RequestMessage?.Body;
        body.Should().Contain("201001234567");
    }
}
