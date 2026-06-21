// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http;
using Headless.ReCaptcha.Contracts;

namespace Tests;

public sealed class ReCaptchaSiteVerifyV3Tests
{
    private static ReCaptchaSiteVerifyRequest Request(string? remoteIp = null) =>
        new() { Response = "the-token", RemoteIp = remoteIp };

    [Fact]
    public async Task should_return_deserialized_response_on_success()
    {
        var (service, _) = TestHelpers.CreateV3(_ =>
            TestHelpers.Json("""{"success":true,"score":0.9,"action":"login","hostname":"localhost"}""")
        );

        var response = await service.VerifyAsync(Request());

        response.Success.Should().BeTrue();
        response.Score.Should().Be(0.9f);
        response.Action.Should().Be("login");
        response.HostName.Should().Be("localhost");
    }

    [Fact]
    public async Task should_post_secret_token_and_remote_ip()
    {
        var (service, handler) = TestHelpers.CreateV3(_ => TestHelpers.Json("""{"success":true}"""));

        await service.VerifyAsync(Request(remoteIp: "203.0.113.7"));

        handler.LastRequestBody.Should().Contain("secret=secret");
        handler.LastRequestBody.Should().Contain("response=the-token");
        handler.LastRequestBody.Should().Contain("remoteip=203.0.113.7");
    }

    [Fact]
    public async Task should_throw_http_request_exception_on_non_success_status()
    {
        var (service, _) = TestHelpers.CreateV3(_ => TestHelpers.Json("boom", HttpStatusCode.InternalServerError));

        var act = () => service.VerifyAsync(Request());

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task should_throw_invalid_operation_when_body_deserializes_to_null()
    {
        var (service, _) = TestHelpers.CreateV3(_ => TestHelpers.Json("null"));

        var act = () => service.VerifyAsync(Request());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task enforcing_overload_should_be_valid_when_success_action_and_score_match()
    {
        var (service, _) = TestHelpers.CreateV3(_ =>
            TestHelpers.Json("""{"success":true,"score":0.9,"action":"login"}""")
        );

        var result = await service.VerifyAsync(Request(), expectedAction: "login", minimumScore: 0.5f);

        result.IsValid.Should().BeTrue();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task enforcing_overload_should_fail_on_action_mismatch()
    {
        var (service, _) = TestHelpers.CreateV3(_ =>
            TestHelpers.Json("""{"success":true,"score":0.9,"action":"signup"}""")
        );

        var result = await service.VerifyAsync(Request(), expectedAction: "login", minimumScore: 0.5f);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ReCaptchaV3VerificationFailureReason.ActionMismatch);
    }

    [Fact]
    public async Task enforcing_overload_should_fail_when_score_below_threshold()
    {
        var (service, _) = TestHelpers.CreateV3(_ =>
            TestHelpers.Json("""{"success":true,"score":0.3,"action":"login"}""")
        );

        var result = await service.VerifyAsync(Request(), expectedAction: "login", minimumScore: 0.5f);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ReCaptchaV3VerificationFailureReason.ScoreBelowThreshold);
    }

    [Fact]
    public async Task enforcing_overload_should_fail_closed_when_score_missing()
    {
        // success=true but no score: must NOT pass the gate (null < threshold is false in C#, the bug we guard against).
        var (service, _) = TestHelpers.CreateV3(_ => TestHelpers.Json("""{"success":true,"action":"login"}"""));

        var result = await service.VerifyAsync(Request(), expectedAction: "login", minimumScore: 0.5f);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ReCaptchaV3VerificationFailureReason.ScoreBelowThreshold);
    }

    [Fact]
    public async Task enforcing_overload_should_fail_when_unsuccessful()
    {
        var (service, _) = TestHelpers.CreateV3(_ =>
            TestHelpers.Json("""{"success":false,"error-codes":["timeout-or-duplicate"]}""")
        );

        var result = await service.VerifyAsync(Request(), expectedAction: "login", minimumScore: 0.5f);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(ReCaptchaV3VerificationFailureReason.Unsuccessful);
    }

    [Fact]
    public async Task enforcing_overload_should_throw_for_blank_expected_action()
    {
        var (service, _) = TestHelpers.CreateV3(_ => TestHelpers.Json("""{"success":true}"""));

        var act = () => service.VerifyAsync(Request(), expectedAction: " ", minimumScore: 0.5f);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}

public sealed class ReCaptchaSiteVerifyV2Tests
{
    private static ReCaptchaSiteVerifyRequest Request() => new() { Response = "the-token" };

    [Fact]
    public async Task should_return_deserialized_response_on_success()
    {
        var (service, _) = TestHelpers.CreateV2(_ => TestHelpers.Json("""{"success":true,"hostname":"localhost"}"""));

        var response = await service.VerifyAsync(Request());

        response.Success.Should().BeTrue();
        response.HostName.Should().Be("localhost");
    }

    [Fact]
    public async Task should_post_secret_and_token()
    {
        var (service, handler) = TestHelpers.CreateV2(_ => TestHelpers.Json("""{"success":true}"""));

        await service.VerifyAsync(Request());

        handler.LastRequestBody.Should().Contain("secret=secret");
        handler.LastRequestBody.Should().Contain("response=the-token");
    }

    [Fact]
    public async Task should_throw_http_request_exception_on_non_success_status()
    {
        var (service, _) = TestHelpers.CreateV2(_ => TestHelpers.Json("boom", HttpStatusCode.BadGateway));

        var act = () => service.VerifyAsync(Request());

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task should_throw_invalid_operation_when_body_deserializes_to_null()
    {
        var (service, _) = TestHelpers.CreateV2(_ => TestHelpers.Json("null"));

        var act = () => service.VerifyAsync(Request());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
