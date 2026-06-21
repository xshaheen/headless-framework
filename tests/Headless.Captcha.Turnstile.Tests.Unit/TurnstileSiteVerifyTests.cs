// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Captcha;

namespace Tests;

/// <summary>Turnstile-specific verify behavior: idempotency key encoding and cdata surfacing.</summary>
public sealed class TurnstileSiteVerifyTests : IDisposable
{
    private readonly TurnstileVerifierFixture _fixture = new();

    [Fact]
    public async Task idempotency_key_included_in_form_when_supplied()
    {
        var stub = new StubSiteVerifyHandler().EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        var verifier = _fixture.CreateTurnstileVerifier(stub);

        await verifier.VerifyAsync(new TurnstileVerifyRequest { Response = "token", IdempotencyKey = "idem-1" });

        stub.LastRequestBody.Should().Contain("idempotency_key");
        stub.LastRequestBody.Should().Contain("idem-1");
    }

    [Fact]
    public async Task idempotency_key_omitted_from_form_when_absent()
    {
        var stub = new StubSiteVerifyHandler().EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        var verifier = _fixture.CreateTurnstileVerifier(stub);

        await verifier.VerifyAsync(new TurnstileVerifyRequest { Response = "token" });

        stub.LastRequestBody.Should().NotContain("idempotency_key");
    }

    [Fact]
    public async Task cdata_is_surfaced_on_result_which_is_assignable_to_base()
    {
        var stub = new StubSiteVerifyHandler().EnqueueJson(HttpStatusCode.OK, _fixture.SuccessResponseBody);
        var verifier = _fixture.CreateTurnstileVerifier(stub);

        var result = await verifier.VerifyAsync(new TurnstileVerifyRequest { Response = "token" });

        result.CData.Should().Be("session-123");

        // The Turnstile result is a CaptchaVerifyResult — the base view sees pass/fail only.
        CaptchaVerifyResult baseResult = result;
        baseResult.Success.Should().BeTrue();
    }

    public void Dispose() => _fixture.Dispose();
}
