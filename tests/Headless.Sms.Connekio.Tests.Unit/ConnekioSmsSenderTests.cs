// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Connekio;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed class ConnekioSmsSenderTests : TestBase, IClassFixture<SmsWireMockFixture>
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
    public async Task should_succeed_on_success_status_with_empty_body()
    {
        // Regression: a success status must win even when the body is empty.
        _Stub("/single", HttpStatusCode.OK, string.Empty);

        var result = await _CreateSender().SendAsync(SmsRequests.Single(), AbortToken);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_surface_the_response_body_without_guessing_a_kind_on_a_server_error()
    {
        _Stub("/single", HttpStatusCode.InternalServerError, "boom");

        var result = await _CreateSender().SendAsync(SmsRequests.Single(), AbortToken);
        result.Success.Should().BeFalse();
        result.FailureError.Should().Be("boom");

        // Connekio documents no error contract, so a 5xx is not assumed to be retryable.
        result.FailureKind.Should().Be(SmsFailureKind.Unknown);
    }

    [Fact]
    public async Task should_classify_an_unauthorized_response_as_an_auth_failure()
    {
        _Stub("/single", HttpStatusCode.Unauthorized, "nope");

        var result = await _CreateSender().SendAsync(SmsRequests.Single(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.AuthFailure);
    }

    [Fact]
    public async Task should_route_bulk_requests_to_the_batch_endpoint()
    {
        _Stub("/batch", HttpStatusCode.OK, "{}");

        var result = await _CreateSender()
            .SendBulkAsync(SmsRequests.Bulk("hi", (20, "1001"), (20, "1002")), AbortToken);
        result.AllSucceeded.Should().BeTrue();
        result.Results.Should().HaveCount(2);
        _fixture.Server.FindLogEntries(Request.Create().WithPath("/batch").UsingPost()).Should().ContainSingle();
    }

    [Fact]
    public async Task should_mirror_a_bulk_failure_kind_to_every_recipient()
    {
        // Connekio's batch endpoint reports one status, so a 401 must classify every recipient as an auth failure.
        _Stub("/batch", HttpStatusCode.Unauthorized, "nope");

        var result = await _CreateSender()
            .SendBulkAsync(SmsRequests.Bulk("hi", (20, "1001"), (20, "1002")), AbortToken);

        result.AllSucceeded.Should().BeFalse();
        result.AnySucceeded.Should().BeFalse();
        result.Results.Should().HaveCount(2);
        result.Results.Should().AllSatisfy(r => r.Result.FailureKind.Should().Be(SmsFailureKind.AuthFailure));
    }

    [Fact]
    public async Task should_send_every_recipient_in_the_bulk_payload()
    {
        _Stub("/batch", HttpStatusCode.OK, "{}");

        await _CreateSender().SendBulkAsync(SmsRequests.Bulk("hi", (20, "1001"), (20, "1002")), AbortToken);

        var body = _fixture.Server.LogEntries.Single().RequestMessage?.Body;
        body.Should().NotBeNull();
        body.Should().Contain("201001").And.Contain("201002");
    }

    [Fact]
    public async Task should_send_a_basic_authorization_header()
    {
        _Stub("/single", HttpStatusCode.OK, "{}");

        await _CreateSender().SendAsync(SmsRequests.Single(), AbortToken);
        var headers = _fixture.Server.LogEntries.Single().RequestMessage?.Headers;
        headers.Should().ContainKey("Authorization");
        headers!["Authorization"].ToString().Should().Contain("Basic");
    }

    public static TheoryData<Exception> ResilienceRejections { get; } =
        new()
        {
            new TimeoutRejectedException("pipeline timeout"),
            new BrokenCircuitException("circuit open"),
            new RateLimiterRejectedException("rate limiter rejected"),
        };

    [Theory]
    [MemberData(nameof(ResilienceRejections))]
    public async Task should_classify_resilience_rejections_as_transient(Exception exception)
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
        var sender = new ConnekioSmsSender(
            new ThrowingHttpClientFactory(exception),
            options,
            NullLogger<ConnekioSmsSender>.Instance
        );

        var result = await sender.SendAsync(SmsRequests.Single(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.Transient);
    }
}
