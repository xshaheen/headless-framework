// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Captcha;

/// <summary>
/// Verifies Google reCAPTCHA v3 tokens and provides score-based results.
/// </summary>
/// <remarks>
/// <see cref="CaptchaVerifyResult.Success"/> is <see langword="true"/> only when Google accepts the token and it meets
/// <see cref="ReCaptchaOptions.MinimumScore"/>, <see cref="ReCaptchaOptions.ExpectedAction"/>, and
/// <see cref="ReCaptchaOptions.ExpectedHostName"/>.
/// </remarks>
[PublicAPI]
public interface IReCaptchaV3Verifier : ICaptchaVerifier
{
    /// <summary>Verifies the token and returns the reCAPTCHA v3 verification result.</summary>
    /// <param name="request">The verification request containing the response token and optional remote IP address.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The reCAPTCHA v3 verification result.</returns>
    /// <exception cref="HttpRequestException">The site verification HTTP response was unsuccessful.</exception>
    /// <exception cref="InvalidOperationException">The site verification response body cannot be deserialized.</exception>
    new Task<ReCaptchaV3VerifyResult> VerifyAsync(
        CaptchaVerifyRequest request,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Implements <see cref="IReCaptchaV3Verifier"/> for the Google reCAPTCHA v3 site verification endpoint.
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

            return _Result(wire, success: false, wire.ErrorCodes);
        }

        var policyFailures = ReCaptchaVerdictPolicy.Evaluate(
            options,
            wire.HostName,
            wire.Action,
            wire.Score,
            isV3: true
        );

        if (policyFailures.Length > 0)
        {
            logger?.LogReCaptchaPolicyRejected(policyFailures, wire.Score, wire.Action, wire.HostName);

            return _Result(wire, success: false, policyFailures);
        }

        return _Result(wire, success: true, wire.ErrorCodes);
    }

    private static ReCaptchaV3VerifyResult _Result(
        ReCaptchaSiteVerifyV3Response wire,
        bool success,
        IReadOnlyList<string>? errorCodes
    )
    {
        return new ReCaptchaV3VerifyResult
        {
            Success = success,
            ChallengeTimestamp = wire.ChallengeTimeStamp,
            HostName = wire.HostName,
            Action = wire.Action,
            ErrorCodes = errorCodes,
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
