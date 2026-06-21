// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Cequens;
using Headless.Sms.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class CequensSmsSenderTests
{
    private const string _TokenEndpoint = "https://apis.cequens.com/auth/v1/tokens";
    private const string _SendEndpoint = "https://apis.cequens.com/sms/v1/messages";

    private static CequensSmsSender CreateSender(
        TestHttpMessageHandler handler,
        FakeTimeProvider time,
        string? staticToken = null
    )
    {
        var options = Options.Create(
            new CequensSmsOptions
            {
                SingleSmsEndpoint = _SendEndpoint,
                TokenEndpoint = _TokenEndpoint,
                ApiKey = "api-key",
                UserName = "user",
                SenderName = "SENDER",
                Token = staticToken,
            }
        );

        return new CequensSmsSender(
            new TestHttpClientFactory(handler),
            time,
            options,
            NullLogger<CequensSmsSender>.Instance
        );
    }

    private static bool IsTokenEndpoint(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsoluteUri == _TokenEndpoint;
    }

    private static HttpResponseMessage TokenResponse(string token)
    {
        return TestHttpMessageHandler.CreateResponse(
            HttpStatusCode.OK,
            "{\"data\":{\"access_token\":\"" + token + "\"}}"
        );
    }

    [Fact]
    public async Task should_send_successfully_and_cache_the_token_across_sends()
    {
        var time = new FakeTimeProvider();
        var tokenCalls = 0;
        var handler = new TestHttpMessageHandler(request =>
        {
            if (IsTokenEndpoint(request))
            {
                tokenCalls++;

                return TokenResponse("tok-1");
            }

            return TestHttpMessageHandler.CreateResponse(HttpStatusCode.OK, "{}");
        });
        var sender = CreateSender(handler, time);

        var first = await sender.SendAsync(SmsRequests.Single());
        var second = await sender.SendAsync(SmsRequests.Single());

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        tokenCalls.Should().Be(1); // fetched once, reused for the second send
    }

    [Fact]
    public async Task should_invalidate_the_token_and_retry_on_unauthorized()
    {
        var time = new FakeTimeProvider();
        var tokenCalls = 0;
        var sendCalls = 0;
        var handler = new TestHttpMessageHandler(request =>
        {
            if (IsTokenEndpoint(request))
            {
                tokenCalls++;

                return TokenResponse($"tok-{tokenCalls}");
            }

            sendCalls++;

            return sendCalls == 1
                ? TestHttpMessageHandler.CreateResponse(HttpStatusCode.Unauthorized, "unauthorized")
                : TestHttpMessageHandler.CreateResponse(HttpStatusCode.OK, "{}");
        });
        var sender = CreateSender(handler, time);

        var result = await sender.SendAsync(SmsRequests.Single());

        result.Success.Should().BeTrue();
        tokenCalls.Should().Be(2); // initial fetch + re-auth after the 401
        sendCalls.Should().Be(2); // 401, then the retry succeeds
    }

    [Fact]
    public async Task should_fall_back_to_the_static_token_when_sign_in_fails()
    {
        var time = new FakeTimeProvider();
        var handler = new TestHttpMessageHandler(request =>
            IsTokenEndpoint(request)
                ? TestHttpMessageHandler.CreateResponse(HttpStatusCode.InternalServerError, "down")
                : TestHttpMessageHandler.CreateResponse(HttpStatusCode.OK, "{}")
        );
        var sender = CreateSender(handler, time, staticToken: "static-fallback");

        var result = await sender.SendAsync(SmsRequests.Single());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_with_auth_failure_when_no_token_is_available()
    {
        var time = new FakeTimeProvider();
        var handler = new TestHttpMessageHandler(_ =>
            TestHttpMessageHandler.CreateResponse(HttpStatusCode.InternalServerError, "down")
        );
        var sender = CreateSender(handler, time);

        var result = await sender.SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.AuthFailure);
    }
}
