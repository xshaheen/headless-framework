// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Captcha;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>Turnstile-specific verify behavior: idempotency key encoding and cdata surfacing.</summary>
public sealed class TurnstileSiteVerifyTests : TestBase
{
    private readonly TurnstileVerifierFixture _fixture = new();

    protected override ValueTask DisposeAsyncCore()
    {
        _fixture.Dispose();
        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task idempotency_key_included_in_form_when_supplied()
    {
        using var stubSiteVerifyHandler = new StubSiteVerifyHandler();
        var stub = stubSiteVerifyHandler.EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        var verifier = _fixture.CreateTurnstileVerifier(stub);

        await verifier.VerifyAsync(
            new TurnstileVerifyRequest { Response = "token", IdempotencyKey = "idem-1" },
            AbortToken
        );

        stub.LastRequestBody.Should().Contain("idempotency_key");
        stub.LastRequestBody.Should().Contain("idem-1");
    }

    [Fact]
    public async Task idempotency_key_omitted_from_form_when_absent()
    {
        using var stubSiteVerifyHandler = new StubSiteVerifyHandler();
        var stub = stubSiteVerifyHandler.EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        var verifier = _fixture.CreateTurnstileVerifier(stub);

        await verifier.VerifyAsync(new TurnstileVerifyRequest { Response = "token" }, AbortToken);

        stub.LastRequestBody.Should().NotContain("idempotency_key");
    }

    [Fact]
    public async Task cdata_is_surfaced_on_result_which_is_assignable_to_base()
    {
        using var stubSiteVerifyHandler = new StubSiteVerifyHandler();
        var stub = stubSiteVerifyHandler.EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        var verifier = _fixture.CreateTurnstileVerifier(stub);

        var result = await verifier.VerifyAsync(new TurnstileVerifyRequest { Response = "token" }, AbortToken);

        result.CData.Should().Be("session-123");

        // The Turnstile result is a CaptchaVerifyResult — the base view sees pass/fail only.
        CaptchaVerifyResult baseResult = result;
        baseResult.Success.Should().BeTrue();
    }

    [Fact]
    public async Task success_without_optional_fields_is_not_an_error_and_leaves_them_null()
    {
        // A success body that omits hostname/challenge_ts is valid — the base result must not over-promise
        // them as non-null on success (no MemberNotNullWhen contract), and verification must not throw.
        using var stubSiteVerifyHandler = new StubSiteVerifyHandler();
        var stub = stubSiteVerifyHandler.EnqueueJson(HttpStatusCode.OK, """{"success":true}""");
        var verifier = _fixture.CreateTurnstileVerifier(stub);

        var result = await verifier.VerifyAsync(new TurnstileVerifyRequest { Response = "token" }, AbortToken);

        result.Success.Should().BeTrue();
        result.HostName.Should().BeNull();
        result.ChallengeTimestamp.Should().BeNull();
    }

    [Theory]
    [InlineData("other.com", null, "hostname-mismatch")]
    [InlineData(null, "checkout", "action-mismatch")]
    public async Task should_refuse_a_token_solved_on_another_host_or_for_another_action(
        string? expectedHostName,
        string? expectedAction,
        string expectedError
    )
    {
        // given - Cloudflare's success only says the token fits the site key; reCAPTCHA already checked these
        using var stubSiteVerifyHandler = new StubSiteVerifyHandler();
        var stub = stubSiteVerifyHandler.EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        var verifier = _fixture.CreateTurnstileVerifier(
            stub,
            options =>
            {
                options.ExpectedHostName = expectedHostName;
                options.ExpectedAction = expectedAction;
            }
        );

        // when
        var result = await verifier.VerifyAsync(new TurnstileVerifyRequest { Response = "token" }, AbortToken);

        // then
        result.Success.Should().BeFalse();
        result.ErrorCodes.Should().Contain(expectedError);
    }

    [Fact]
    public async Task should_accept_a_token_whose_host_and_action_match_ignoring_host_case()
    {
        using var stubSiteVerifyHandler = new StubSiteVerifyHandler();
        var stub = stubSiteVerifyHandler.EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        var verifier = _fixture.CreateTurnstileVerifier(
            stub,
            static options =>
            {
                options.ExpectedHostName = "EXAMPLE.com";
                options.ExpectedAction = "login";
            }
        );

        var result = await verifier.VerifyAsync(new TurnstileVerifyRequest { Response = "token" }, AbortToken);

        result.Success.Should().BeTrue();
    }

    // -- ICaptchaVerifier explicit-impl idempotency key forwarding (Finding #7) ---------------------------------

    [Fact]
    public async Task explicit_base_verifier_forwards_idempotency_key_when_request_is_turnstile_type()
    {
        using var stubSiteVerifyHandler = new StubSiteVerifyHandler();
        var stub = stubSiteVerifyHandler.EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        var verifier = _fixture.CreateVerifier(stub);

        await verifier.VerifyAsync(new TurnstileVerifyRequest { Response = "token", IdempotencyKey = "x" }, AbortToken);

        stub.LastRequestBody.Should().Contain("idempotency_key");
        stub.LastRequestBody.Should().Contain("x");
    }

    [Fact]
    public async Task explicit_base_verifier_omits_idempotency_key_when_request_is_plain_base_type()
    {
        using var stubSiteVerifyHandler = new StubSiteVerifyHandler();
        var stub = stubSiteVerifyHandler.EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        var verifier = _fixture.CreateVerifier(stub);

        await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        stub.LastRequestBody.Should().NotContain("idempotency_key");
    }
}
