// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// Verifies Cloudflare Turnstile tokens. The typed <see cref="VerifyAsync(TurnstileVerifyRequest,CancellationToken)"/>
/// overload returns a <see cref="TurnstileVerifyResult"/> exposing Turnstile-only data (<c>cdata</c>, Enterprise
/// <c>metadata</c>); resolving the base <see cref="ICaptchaVerifier"/> yields the pass/fail result only.
/// </summary>
[PublicAPI]
public interface ITurnstileVerifier : ICaptchaVerifier
{
    /// <summary>Verifies the Turnstile token, returning the Turnstile-specific result.</summary>
    /// <param name="request">The Turnstile verification request (token, optional remote IP, optional idempotency key).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The Turnstile verification result.</returns>
    /// <exception cref="HttpRequestException">The siteverify HTTP response was unsuccessful.</exception>
    /// <exception cref="InvalidOperationException">The siteverify response body could not be deserialized.</exception>
    Task<TurnstileVerifyResult> VerifyAsync(
        TurnstileVerifyRequest request,
        CancellationToken cancellationToken = default
    );
}
