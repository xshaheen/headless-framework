// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Captcha;

namespace Tests;

/// <summary>
/// reCAPTCHA v3's score is provider-specific: reachable through <see cref="IReCaptchaV3Verifier"/> (returns
/// <see cref="ReCaptchaV3VerifyResult"/>), while the generic <see cref="ICaptchaVerifier"/> view is pass/fail only
/// and requires a cast to read the score (AE2).
/// </summary>
public sealed class ReCaptchaV3ScoreTests : IDisposable
{
    private readonly ReCaptchaV3VerifierFixture _fixture = new();

    [Fact]
    public async Task score_is_reachable_through_the_v3_verifier()
    {
        var stub = new StubSiteVerifyHandler().EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        var verifier = _fixture.CreateV3Verifier(stub);

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" });

        result.Score.Should().BeApproximately(0.9f, 0.0001f);
    }

    [Fact]
    public async Task generic_verifier_returns_pass_fail_result_score_requires_cast()
    {
        var stub = new StubSiteVerifyHandler().EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        ICaptchaVerifier verifier = _fixture.CreateVerifier(stub);

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" });

        // The base contract exposes pass/fail only; the score is reachable only by casting to the v3 result.
        result.Success.Should().BeTrue();
        result.Should().BeOfType<ReCaptchaV3VerifyResult>();
        ((ReCaptchaV3VerifyResult)result).Score.Should().BeApproximately(0.9f, 0.0001f);
    }

    [Fact]
    public async Task low_score_result_drives_caller_side_gating()
    {
        // Demonstrates the result-shape: callers compare Score against their own threshold.
        var stub = new StubSiteVerifyHandler().EnqueueJson(
            HttpStatusCode.OK,
            """
            {"success":true,"challenge_ts":"2026-06-21T10:00:00Z","hostname":"example.com","score":0.1,"action":"login","error-codes":[]}
            """
        );
        var verifier = _fixture.CreateV3Verifier(stub);

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" });

        var passesThreshold = result.Success && result.Score >= 0.5f;
        passesThreshold.Should().BeFalse();
    }

    public void Dispose() => _fixture.Dispose();
}
