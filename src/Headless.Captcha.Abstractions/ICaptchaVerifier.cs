// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// Verifies a CAPTCHA response token against the provider's server-side endpoint. The result is normalized to the
/// fields every provider returns — pass/fail, hostname, challenge timestamp, action, and error codes. Provider-only
/// data (reCAPTCHA v3's score, Turnstile's cdata) is exposed on the provider's derived interface and result type,
/// never on this base contract.
/// </summary>
/// <remarks>
/// <para>
/// <b>Error model (throw, not return):</b> a returned <see cref="CaptchaVerifyResult"/> always represents a
/// completed verification — <see cref="CaptchaVerifyResult.Success"/> distinguishes a passing token from a rejected
/// one (with <see cref="CaptchaVerifyResult.ErrorCodes"/>). A transport or deserialization failure is
/// <em>exceptional</em> and <b>throws</b> (see <see cref="VerifyAsync"/>) rather than being folded into a "failed"
/// result. The rationale: an unverifiable request is a bug or outage the caller must not silently treat as a failed
/// challenge (which could let a bad token through on a lenient code path). This is the deliberate opposite of
/// <c>ISmsSender.SendAsync</c>, which <em>returns</em> failure results for the same class of remote-call failure
/// because a rejected send is ordinary, expected data rather than an error.
/// </para>
/// </remarks>
[PublicAPI]
public interface ICaptchaVerifier
{
    /// <summary>Verifies the response token and returns the normalized outcome.</summary>
    /// <param name="request">The verification request carrying the response token and an optional remote IP.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The normalized verification result.</returns>
    /// <exception cref="HttpRequestException">The siteverify HTTP response was unsuccessful.</exception>
    /// <exception cref="InvalidOperationException">The siteverify response body could not be deserialized.</exception>
    Task<CaptchaVerifyResult> VerifyAsync(CaptchaVerifyRequest request, CancellationToken cancellationToken = default);
}
