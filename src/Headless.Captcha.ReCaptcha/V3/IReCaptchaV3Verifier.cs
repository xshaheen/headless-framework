// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
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

/// <summary>
/// <see cref="IReCaptchaV3Verifier"/> over Google's <c>recaptcha/api/siteverify</c> endpoint. Registered per slot,
/// so it resolves its named options and HTTP client by the registration name.
/// </summary>
internal sealed class ReCaptchaSiteVerifyV3(
    string name,
    IOptionsMonitor<ReCaptchaOptions> optionsMonitor,
    IHttpClientFactory clientFactory,
    ILogger<ReCaptchaSiteVerifyV3>? logger
) : IReCaptchaV3Verifier
{
    public async Task<ReCaptchaV3VerifyResult> VerifyAsync(
        CaptchaVerifyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        var options = optionsMonitor.Get(name);
        var client = clientFactory.CreateClient(name);

        var wire = await ReCaptchaSiteVerifyClient
            .SendAsync(
                client,
                options.SiteSecret,
                request,
                ReCaptchaJsonSerializerContext.Default.ReCaptchaSiteVerifyV3Response,
                logger,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!wire.Success)
        {
            logger?.LogReCaptchaFailure(wire);
        }

        return new ReCaptchaV3VerifyResult
        {
            Success = wire.Success,
            ChallengeTimestamp = wire.ChallengeTimeStamp,
            HostName = wire.HostName,
            Action = wire.Action,
            ErrorCodes = wire.ErrorCodes,
            Score = wire.Score ?? 0f,
        };
    }

    async Task<CaptchaVerifyResult> ICaptchaVerifier.VerifyAsync(
        CaptchaVerifyRequest request,
        CancellationToken cancellationToken
    )
    {
        return await VerifyAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
