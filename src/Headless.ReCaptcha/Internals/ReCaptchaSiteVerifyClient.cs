// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.ReCaptcha.Contracts;
using Microsoft.Extensions.Logging;

namespace Headless.ReCaptcha.Internals;

/// <summary>Shared HTTP transport for the v2/v3 siteverify call. Owns the form build, POST, error handling, and deserialization.</summary>
internal static class ReCaptchaSiteVerifyClient
{
    private static readonly Uri _SiteVerifyUri = new("recaptcha/api/siteverify", UriKind.Relative);

    public static async Task<TResponse> SendAsync<TResponse>(
        HttpClient client,
        ReCaptchaOptions options,
        ReCaptchaSiteVerifyRequest request,
        ILogger logger,
        CancellationToken cancellationToken
    )
        where TResponse : class
    {
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
        using var httpResponseMessage = await client
            .PostAsync(_SiteVerifyUri, content, cancellationToken)
            .ConfigureAwait(false);

        if (!httpResponseMessage.IsSuccessStatusCode)
        {
            if (logger.IsEnabled(LogLevel.Warning))
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

        var response = await JsonSerializer
            .DeserializeAsync<TResponse>(
                utf8Json: responseStream,
                options: ReCaptchaJsonOptions.JsonOptions,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return response
            ?? throw new InvalidOperationException("Failed to deserialize reCAPTCHA response. Response was null.");
    }
}
