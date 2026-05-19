// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.ReCaptcha.Contracts;
using Headless.ReCaptcha.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.ReCaptcha.V3;

/// <summary>
/// reCAPTCHA v3 returns a score for each request without user friction. The score is based
/// on interactions with your site and enables you to take an appropriate action for your site.
/// </summary>
public interface IReCaptchaSiteVerifyV3
{
    /// <summary>Validate Recapture token.</summary>
    /// <param name="request">The verification request containing the reCAPTCHA response token.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="HttpRequestException">The HTTP response is unsuccessful.</exception>
    Task<ReCaptchaSiteVerifyV3Response> VerifyAsync(
        ReCaptchaSiteVerifyRequest request,
        CancellationToken cancellationToken = default
    );
}

public sealed class ReCaptchaSiteVerifyV3(
    IOptionsSnapshot<ReCaptchaOptions> optionsAccessor,
    IHttpClientFactory clientFactory,
    ILogger<ReCaptchaSiteVerifyV3> logger
) : IReCaptchaSiteVerifyV3
{
    private readonly Uri _siteVerifyUri = new("recaptcha/api/siteverify", UriKind.Relative);
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(ReCaptchaSetup.V3Name);
    private readonly HttpClient _client = clientFactory.CreateClient(ReCaptchaSetup.V3Name);

    public async Task<ReCaptchaSiteVerifyV3Response> VerifyAsync(
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
            .ConfigureAwait(false);

        if (!httpResponseMessage.IsSuccessStatusCode)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                var responseBody = await httpResponseMessage
                    .Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                logger.LogRecaptchaVerificationFailed(httpResponseMessage.StatusCode, responseBody);
            }

            httpResponseMessage.EnsureSuccessStatusCode();
        }

        await using var responseStream = await httpResponseMessage
            .Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var response = await JsonSerializer
            .DeserializeAsync<ReCaptchaSiteVerifyV3Response>(
                utf8Json: responseStream,
                options: ReCaptchaJsonOptions.JsonOptions,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        if (response?.Success is not true)
        {
            logger.LogReCaptchaFailure(response);
        }

        return response
            ?? throw new InvalidOperationException("Failed to deserialize reCAPTCHA response. Response was null.");
    }
}

internal static partial class ReCaptchaSiteVerifyV3Log
{
    [LoggerMessage(
        EventId = 1,
        EventName = "RecaptchaV3VerificationFailed",
        Level = LogLevel.Information,
        Message = "Recaptcha verification failed with status code {StatusCode} and response {Response}"
    )]
    public static partial void LogRecaptchaVerificationFailed(
        this ILogger logger,
        HttpStatusCode statusCode,
        string response
    );
}
