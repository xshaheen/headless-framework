// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Captcha;

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
