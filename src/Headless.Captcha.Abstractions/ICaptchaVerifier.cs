// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// Verifies a CAPTCHA response token against a provider endpoint and returns normalized outcome data.
/// </summary>
/// <remarks>
/// A returned <see cref="CaptchaVerifyResult"/> represents a completed verification, where
/// <see cref="CaptchaVerifyResult.Success"/> indicates token validity. Remote transport failures or malformed responses
/// throw exceptions rather than returning failed results.
/// </remarks>
[PublicAPI]
public interface ICaptchaVerifier
{
    /// <summary>Verifies the response token and returns the normalized outcome.</summary>
    /// <param name="request">The verification request containing the response token and an optional remote IP address.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The normalized verification result.</returns>
    /// <exception cref="HttpRequestException">The site verification HTTP response was unsuccessful.</exception>
    /// <exception cref="InvalidOperationException">The site verification response body cannot be deserialized.</exception>
    Task<CaptchaVerifyResult> VerifyAsync(CaptchaVerifyRequest request, CancellationToken cancellationToken = default);
}
