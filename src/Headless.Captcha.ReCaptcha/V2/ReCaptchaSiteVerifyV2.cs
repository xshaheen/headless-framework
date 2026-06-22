// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// <see cref="ICaptchaVerifier"/> over Google's reCAPTCHA v2 <c>recaptcha/api/siteverify</c> endpoint. v2 carries no
/// provider-only data, so it implements the plain base contract. Registered per slot, so it resolves its named
/// options and HTTP client by the registration name.
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
