// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Captcha;

/// <summary>
/// Verifies Google reCAPTCHA v3 tokens. reCAPTCHA v3 returns a score for each request without user friction; the
/// typed <see cref="VerifyAsync(CaptchaVerifyRequest,CancellationToken)"/> overload exposes that score on
/// <see cref="ReCaptchaV3VerifyResult"/>. Resolving the base <see cref="ICaptchaVerifier"/> yields pass/fail only.
/// </summary>
[PublicAPI]
public interface IReCaptchaV3Verifier : ICaptchaVerifier
{
    /// <summary>Verifies the token, returning the reCAPTCHA v3 result (including the numeric score).</summary>
    /// <param name="request">The verification request (token + optional remote IP).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The reCAPTCHA v3 verification result.</returns>
    /// <exception cref="HttpRequestException">The siteverify HTTP response was unsuccessful.</exception>
    /// <exception cref="InvalidOperationException">The siteverify response body could not be deserialized.</exception>
    new Task<ReCaptchaV3VerifyResult> VerifyAsync(
        CaptchaVerifyRequest request,
        CancellationToken cancellationToken = default
    );
}
