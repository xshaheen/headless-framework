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
    private static readonly Uri _SiteVerifyUri = new("recaptcha/api/siteverify", UriKind.Relative);

    public async Task<CaptchaVerifyResult> VerifyAsync(
        CaptchaVerifyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        var options = optionsMonitor.Get(name);

        List<KeyValuePair<string, string>> formData =
        [
            new("secret", options.SiteSecret),
            new("response", request.Response),
        ];

        if (request.RemoteIp is not null)
        {
            formData.Add(new("remoteip", request.RemoteIp));
        }

        using var content = new FormUrlEncodedContent(formData);
        var client = clientFactory.CreateClient(name);

        using var httpResponseMessage = await client
            .PostAsync(_SiteVerifyUri, content, cancellationToken)
            .ConfigureAwait(false);

        if (!httpResponseMessage.IsSuccessStatusCode)
        {
            if (logger?.IsEnabled(LogLevel.Information) is true)
            {
                var responseBody = await httpResponseMessage
                    .Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                logger.LogReCaptchaHttpFailure(httpResponseMessage.StatusCode, responseBody);
            }

            httpResponseMessage.EnsureSuccessStatusCode();
        }

        await using var responseStream = await httpResponseMessage
            .Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        ReCaptchaSiteVerifyV2Response? wire;

        try
        {
            wire = await JsonSerializer
                .DeserializeAsync(
                    responseStream,
                    ReCaptchaJsonSerializerContext.Default.ReCaptchaSiteVerifyV2Response,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to deserialize reCAPTCHA siteverify response.", ex);
        }

        if (wire is null)
        {
            throw new InvalidOperationException("Failed to deserialize reCAPTCHA response. Response was null.");
        }

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
