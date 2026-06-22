// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// Verifies a CAPTCHA response token against the provider's server-side endpoint. The result is normalized to the
/// fields every provider returns — pass/fail, hostname, challenge timestamp, action, and error codes. Provider-only
/// data (reCAPTCHA v3's score, Turnstile's cdata) is exposed on the provider's derived interface and result type,
/// never on this base contract.
/// </summary>
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
