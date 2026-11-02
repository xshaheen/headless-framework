// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Captcha;

/// <summary>
/// Implements <see cref="ICaptchaVerifier"/> for the Google reCAPTCHA v2 site verification endpoint. A token Google
/// accepts still fails when it was solved on a host other than <see cref="ReCaptchaOptions.ExpectedHostName"/>.
/// </summary>
internal sealed class ReCaptchaSiteVerifyV2(
    string name,
    IOptionsMonitor<ReCaptchaOptions> optionsMonitor,
    IHttpClientFactory clientFactory,
    ILogger<ReCaptchaSiteVerifyV2>? logger
) : ICaptchaVerifier
{
    public async Task<CaptchaVerifyResult> VerifyAsync(
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
                ReCaptchaJsonSerializerContext.Default.ReCaptchaSiteVerifyV2Response,
                logger,
                cancellationToken
            )
            .ConfigureAwait(false);

        var success = wire.Success;
        IReadOnlyList<string>? errorCodes = wire.ErrorCodes;

        if (!success)
        {
            logger?.LogReCaptchaFailure(wire);
        }
        else
        {
            var policyFailures = ReCaptchaVerdictPolicy.Evaluate(
                options,
                wire.HostName,
                action: null,
                score: null,
                isV3: false
            );

            if (policyFailures.Length > 0)
            {
                logger?.LogReCaptchaPolicyRejected(policyFailures, score: null, action: null, wire.HostName);
                success = false;
                errorCodes = policyFailures;
            }
        }

        return new ReCaptchaV2VerifyResult
        {
            Success = success,
            ChallengeTimestamp = wire.ChallengeTimeStamp,
            HostName = wire.HostName,
            ErrorCodes = errorCodes,
        };
    }
}
