// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Captcha;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Google's <c>success</c> flag only says the token is valid for the site key. The verdict callers act on must also
/// hold the configured score threshold, action, and hostname.
/// </summary>
public sealed class ReCaptchaVerdictPolicyTests : TestBase
{
    private readonly ReCaptchaV3VerifierFixture _v3 = new();
    private readonly ReCaptchaV2VerifierFixture _v2 = new();

    protected override ValueTask DisposeAsyncCore()
    {
        _v3.Dispose();
        _v2.Dispose();
        return base.DisposeAsyncCore();
    }

    private static string _V3Body(float score, string action = "login", string hostname = "example.com")
    {
        return $$"""
            {"success":true,"challenge_ts":"2026-06-21T10:00:00Z","hostname":"{{hostname}}","score":{{score.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            )}},"action":"{{action}}","error-codes":[]}
            """;
    }

    [Fact]
    public async Task low_score_fails_the_generic_verifier_with_the_default_threshold()
    {
        using var handler = new StubSiteVerifyHandler();
        handler.EnqueueJson(HttpStatusCode.OK, _V3Body(0.1f));
        var verifier = _v3.CreateVerifier(handler);

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        result.Success.Should().BeFalse();
        result.ErrorCodes.ToReCaptchaErrors().Should().Equal(ReCaptchaError.ScoreTooLow);
    }

    [Fact]
    public async Task score_at_the_threshold_passes()
    {
        using var handler = new StubSiteVerifyHandler();
        handler.EnqueueJson(HttpStatusCode.OK, _V3Body(0.5f));
        var verifier = _v3.CreateV3Verifier(handler);

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        result.Success.Should().BeTrue();
        result.ErrorCodes.Should().BeEmpty();
    }

    [Fact]
    public async Task configured_minimum_score_applies_to_the_v3_verifier()
    {
        using var handler = new StubSiteVerifyHandler();
        handler.EnqueueJson(HttpStatusCode.OK, _V3Body(0.7f));
        var verifier = _v3.CreateV3Verifier(handler, o => o.MinimumScore = 0.8f);

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        result.Success.Should().BeFalse();
        result.Score.Should().BeApproximately(0.7f, 0.0001f);
    }

    [Fact]
    public async Task missing_score_fails_closed()
    {
        using var handler = new StubSiteVerifyHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            """{"success":true,"hostname":"example.com","action":"login","error-codes":[]}"""
        );
        var verifier = _v3.CreateVerifier(handler);

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task action_mismatch_fails()
    {
        using var handler = new StubSiteVerifyHandler();
        handler.EnqueueJson(HttpStatusCode.OK, _V3Body(0.9f, action: "login"));
        var verifier = _v3.CreateVerifier(handler, o => o.ExpectedAction = "signup");

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        result.Success.Should().BeFalse();
        result.ErrorCodes.ToReCaptchaErrors().Should().Equal(ReCaptchaError.ActionMismatch);
    }

    [Fact]
    public async Task matching_action_and_hostname_pass()
    {
        using var handler = new StubSiteVerifyHandler();
        handler.EnqueueJson(HttpStatusCode.OK, _V3Body(0.9f, action: "signup", hostname: "Example.com"));
        var verifier = _v3.CreateVerifier(
            handler,
            o =>
            {
                o.ExpectedAction = "signup";
                o.ExpectedHostName = "example.com";
            }
        );

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task v3_hostname_mismatch_fails()
    {
        using var handler = new StubSiteVerifyHandler();
        handler.EnqueueJson(HttpStatusCode.OK, _V3Body(0.9f, hostname: "attacker.example"));
        var verifier = _v3.CreateVerifier(handler, o => o.ExpectedHostName = "example.com");

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        result.Success.Should().BeFalse();
        result.ErrorCodes.ToReCaptchaErrors().Should().Equal(ReCaptchaError.HostNameMismatch);
    }

    [Fact]
    public async Task v2_hostname_mismatch_fails()
    {
        using var handler = new StubSiteVerifyHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            """{"success":true,"challenge_ts":"2026-06-21T10:00:00Z","hostname":"attacker.example"}"""
        );
        var verifier = _v2.CreateVerifier(handler, o => o.ExpectedHostName = "example.com");

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        result.Success.Should().BeFalse();
        result.ErrorCodes.ToReCaptchaErrors().Should().Equal(ReCaptchaError.HostNameMismatch);
    }

    [Fact]
    public async Task rejected_token_keeps_google_error_codes_only()
    {
        using var handler = new StubSiteVerifyHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """{"success":false,"error-codes":["timeout-or-duplicate"]}""");
        var verifier = _v3.CreateVerifier(handler, o => o.ExpectedAction = "signup");

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        result.Success.Should().BeFalse();
        result.ErrorCodes.Should().Equal("timeout-or-duplicate");
    }
}
