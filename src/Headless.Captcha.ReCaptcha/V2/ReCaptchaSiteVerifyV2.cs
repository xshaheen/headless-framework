// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Captcha;

/// <summary>
/// Implements <see cref="ICaptchaVerifier"/> for the Google reCAPTCHA v2 site verification endpoint.
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

        if (!wire.Success)
        {
            logger?.LogReCaptchaFailure(wire);
        }

        return new ReCaptchaV2VerifyResult
        {
            Success = wire.Success,
            ChallengeTimestamp = wire.ChallengeTimeStamp,
            HostName = wire.HostName,
            ErrorCodes = wire.ErrorCodes,
        };
    }
}
