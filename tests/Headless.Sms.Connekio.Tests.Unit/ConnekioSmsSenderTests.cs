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

public sealed class ConnekioSmsSenderTests : IClassFixture<SmsWireMockFixture>
{
    private readonly SmsWireMockFixture _fixture;

    public ConnekioSmsSenderTests(SmsWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private ConnekioSmsSender CreateSender(string singlePath = "/single", string batchPath = "/batch")
    {
        var options = Options.Create(
            new ConnekioSmsOptions
            {
                SingleSmsEndpoint = $"{_fixture.BaseUrl}{singlePath}",
                BatchSmsEndpoint = $"{_fixture.BaseUrl}{batchPath}",
                Sender = "SENDER",
                AccountId = "acc",
                UserName = "user",
                Password = "pass",
            }
        );

        return new ConnekioSmsSender(_fixture.HttpClientFactory, options, NullLogger<ConnekioSmsSender>.Instance);
    }

    private void Stub(string path, HttpStatusCode statusCode, string body)
    {
        _fixture
            .Server.Given(Request.Create().WithPath(path).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(statusCode).WithBody(body));
    }

    [Fact]
    public async Task should_succeed_on_success_status_with_empty_body()
    {
        // Regression: a success status must win even when the body is empty.
        Stub("/single", HttpStatusCode.OK, string.Empty);

        var result = await CreateSender().SendAsync(SmsRequests.Single());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_surface_the_response_body_and_classify_a_server_error()
    {
        Stub("/single", HttpStatusCode.InternalServerError, "boom");

        var result = await CreateSender().SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
        result.FailureError.Should().Be("boom");
        result.FailureKind.Should().Be(SmsFailureKind.Transient);
    }

    [Fact]
    public async Task should_classify_an_unauthorized_response_as_an_auth_failure()
    {
        Stub("/single", HttpStatusCode.Unauthorized, "nope");

        var result = await CreateSender().SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.AuthFailure);
    }

    [Fact]
    public async Task should_route_batch_requests_to_the_batch_endpoint()
    {
        Stub("/batch", HttpStatusCode.OK, "{}");

        var result = await CreateSender().SendAsync(SmsRequests.Batch("hi", (20, "1001"), (20, "1002")));

        result.Success.Should().BeTrue();
        _fixture.Server.FindLogEntries(Request.Create().WithPath("/batch").UsingPost()).Should().ContainSingle();
    }

    [Fact]
    public async Task should_send_a_basic_authorization_header()
    {
        Stub("/single", HttpStatusCode.OK, "{}");

        await CreateSender().SendAsync(SmsRequests.Single());

        var headers = _fixture.Server.LogEntries.Single().RequestMessage?.Headers;
        headers.Should().ContainKey("Authorization");
        headers!["Authorization"].ToString().Should().Contain("Basic");
    }
}
