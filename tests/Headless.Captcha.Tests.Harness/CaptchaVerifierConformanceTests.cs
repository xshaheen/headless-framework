// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http;
using Headless.Captcha;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// The cross-provider verifier conformance contract. Every captcha provider derives this base with its own
/// <see cref="ICaptchaVerifierFixture"/> and inherits these scenarios — round-trip success, well-formed rejection,
/// HTTP failure, null-body deserialization failure, cancellation, and conditional <c>remoteip</c> encoding.
/// </summary>
/// <typeparam name="TFixture">The provider's conformance fixture.</typeparam>
public abstract class CaptchaVerifierConformanceTests<TFixture> : TestBase, IClassFixture<TFixture>
    where TFixture : class, ICaptchaVerifierFixture
{
    private readonly TFixture _fixture;

    protected CaptchaVerifierConformanceTests(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task valid_token_returns_success_with_common_fields()
    {
        var stub = new StubSiteVerifyHandler().EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        var verifier = _fixture.CreateVerifier(stub);

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "valid-token" }, AbortToken);

        result.Success.Should().BeTrue();
        result.HostName.Should().NotBeNull();
        result.ChallengeTimestamp.Should().NotBeNull();
    }

    [Fact]
    public async Task rejected_token_returns_failure_with_error_codes()
    {
        var stub = new StubSiteVerifyHandler().EnqueueJson(HttpStatusCode.OK, _fixture.RejectedResponseBody);
        var verifier = _fixture.CreateVerifier(stub);

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "rejected-token" }, AbortToken);

        result.Success.Should().BeFalse();
        result.ErrorCodes.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task non_success_http_status_throws_http_request_exception()
    {
        // 400 is not retried by the standard resilience handler, so the failure surfaces immediately.
        var stub = new StubSiteVerifyHandler().EnqueueJson(HttpStatusCode.BadRequest, "{}");
        var verifier = _fixture.CreateVerifier(stub);

        var act = async () => await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task null_json_body_throws_invalid_operation()
    {
        var stub = new StubSiteVerifyHandler().EnqueueJson(HttpStatusCode.OK, "null");
        var verifier = _fixture.CreateVerifier(stub);

        var act = async () => await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task already_cancelled_token_throws_and_makes_no_http_call()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var stub = new StubSiteVerifyHandler().EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        var verifier = _fixture.CreateVerifier(stub);

        var act = async () => await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        stub.InvocationCount.Should().Be(0);
    }

    [Fact]
    public async Task malformed_json_body_on_http_200_throws_invalid_operation()
    {
        // A non-JSON HTTP 200 (e.g. an HTML error page) must surface as InvalidOperationException, not a raw
        // JsonException — callers only need to handle the documented exception contract.
        var stub = new StubSiteVerifyHandler().EnqueueJson(HttpStatusCode.OK, "<html>oops</html>");
        var verifier = _fixture.CreateVerifier(stub);

        var act = async () => await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task remote_ip_included_only_when_supplied()
    {
        var withIpStub = new StubSiteVerifyHandler().EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        var withIpVerifier = _fixture.CreateVerifier(withIpStub);
        await withIpVerifier.VerifyAsync(
            new CaptchaVerifyRequest { Response = "token", RemoteIp = "203.0.113.5" },
            AbortToken
        );

        withIpStub.LastRequestBody.Should().Contain("remoteip");
        withIpStub.LastRequestBody.Should().Contain("203.0.113.5");

        var withoutIpStub = new StubSiteVerifyHandler().EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        var withoutIpVerifier = _fixture.CreateVerifier(withoutIpStub);
        await withoutIpVerifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        withoutIpStub.LastRequestBody.Should().NotContain("remoteip");
    }
}
