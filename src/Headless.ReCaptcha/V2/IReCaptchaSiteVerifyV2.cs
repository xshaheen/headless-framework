// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.ReCaptcha.Contracts;
using Headless.ReCaptcha.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.ReCaptcha.V2;

/// <summary>
/// Verify requests with a user challenge. It has three types of challenges:
/// <list type="bullet">
///   <item>Checkbox: Validate requests with "I'm not a robot" checkbox challenge.</item>
///   <item>Invisible: Validate requests in the background.</item>
///  <item>Android: Validate requests in your android app.</item>
/// </list>
/// </summary>
public interface IReCaptchaSiteVerifyV2
{
    /// <summary>Validate Recapture token.</summary>
    /// <param name="request">The verification request containing the reCAPTCHA response token.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="HttpRequestException">The HTTP response is unsuccessful.</exception>
    Task<ReCaptchaSiteVerifyV2Response> VerifyAsync(
        ReCaptchaSiteVerifyRequest request,
        CancellationToken cancellationToken = default
    );
}

public sealed class ReCaptchaSiteVerifyV2(
    IOptionsSnapshot<ReCaptchaOptions> optionsAccessor,
    IHttpClientFactory clientFactory,
    ILogger<ReCaptchaSiteVerifyV2> logger
) : IReCaptchaSiteVerifyV2
{
    private readonly Uri _siteVerifyUri = new("recaptcha/api/siteverify", UriKind.Relative);

    private readonly HttpClient _client = clientFactory.CreateClient(ReCaptchaSetup.V2Name);
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(ReCaptchaSetup.V2Name);

    public async Task<ReCaptchaSiteVerifyV2Response> VerifyAsync(
        ReCaptchaSiteVerifyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        List<KeyValuePair<string, string>> formData =
        [
            new("secret", _options.SiteSecret),
            new("response", request.Response),
        ];

        if (request.RemoteIp is not null)
        {
            formData.Add(new("remoteip", request.RemoteIp));
        }

        using var content = new FormUrlEncodedContent(formData);
        using var httpResponseMessage = await _client
            .PostAsync(_siteVerifyUri, content, cancellationToken)
            .AnyContext();

        if (!httpResponseMessage.IsSuccessStatusCode)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Recaptcha verification failed with status code {StatusCode} and response {Response}",
                    httpResponseMessage.StatusCode,
                    await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken).AnyContext()
                );
            }

            httpResponseMessage.EnsureSuccessStatusCode();
        }

        await using var responseStream = await httpResponseMessage
            .Content.ReadAsStreamAsync(cancellationToken)
            .AnyContext();

        var response = await JsonSerializer
            .DeserializeAsync<ReCaptchaSiteVerifyV2Response>(
                utf8Json: responseStream,
                options: ReCaptchaJsonOptions.JsonOptions,
                cancellationToken: cancellationToken
            )
            .AnyContext();

        if (response?.Success is not true)
        {
            logger.LogReCaptchaFailure(response);
        }

        return response
            ?? throw new InvalidOperationException("Failed to deserialize reCAPTCHA response. Response was null.");
    }
}
