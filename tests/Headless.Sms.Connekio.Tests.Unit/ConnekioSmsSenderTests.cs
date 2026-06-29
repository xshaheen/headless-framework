// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Connekio;
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

    private ConnekioSmsSender _CreateSender(string singlePath = "/single", string batchPath = "/batch")
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

    private void _Stub(string path, HttpStatusCode statusCode, string body)
    {
        _fixture
            .Server.Given(Request.Create().WithPath(path).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(statusCode).WithBody(body));
    }

    [Fact]
    public async Task should_succeed_on_success_status()
    {
        _Stub("/single", HttpStatusCode.OK, "{}");

        var result = await _CreateSender().SendAsync(SmsRequests.Single());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_succeed_on_success_status_with_empty_body()
    {
        // Regression: a success status must win even when the body is empty.
        _Stub("/single", HttpStatusCode.OK, string.Empty);

        var result = await _CreateSender().SendAsync(SmsRequests.Single());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_on_non_success_status()
    {
        _Stub("/single", HttpStatusCode.InternalServerError, "boom");

        var result = await _CreateSender().SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task should_route_batch_requests_to_the_batch_endpoint()
    {
        _Stub("/batch", HttpStatusCode.OK, "{}");

        var result = await _CreateSender().SendAsync(SmsRequests.Batch("hi", (20, "1001"), (20, "1002")));

        result.Success.Should().BeTrue();
        _fixture.Server.FindLogEntries(Request.Create().WithPath("/batch").UsingPost()).Should().ContainSingle();
    }

    [Fact]
    public async Task should_send_a_basic_authorization_header()
    {
        _Stub("/single", HttpStatusCode.OK, "{}");

        await _CreateSender().SendAsync(SmsRequests.Single());

        var headers = _fixture.Server.LogEntries.Single().RequestMessage?.Headers;
        headers.Should().ContainKey("Authorization");
        headers!["Authorization"].ToString().Should().Contain("Basic");
    }

    [Fact]
    public async Task should_return_transient_failure_on_transport_fault()
    {
        var options = Options.Create(
            new ConnekioSmsOptions
            {
                SingleSmsEndpoint = "http://localhost:1/single",
                BatchSmsEndpoint = "http://localhost:1/batch",
                Sender = "SENDER",
                AccountId = "acc",
                UserName = "user",
                Password = "pass",
            }
        );
        var sender = new ConnekioSmsSender(_fixture.HttpClientFactory, options, NullLogger<ConnekioSmsSender>.Instance);

        var result = await sender.SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.Transient);
    }

    [Fact]
    public async Task should_propagate_cancellation()
    {
        _Stub("/single", HttpStatusCode.OK, "{}");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await _CreateSender().SendAsync(SmsRequests.Single(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
