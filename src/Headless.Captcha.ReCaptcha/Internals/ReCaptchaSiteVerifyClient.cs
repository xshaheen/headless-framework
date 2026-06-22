// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// Shared HTTP transport for the v2/v3 siteverify call. Owns the form build, POST, HTTP error handling, and
/// deserialization (including the documented <see cref="JsonException"/> → <see cref="InvalidOperationException"/>
/// and null-body contract). Each verifier supplies its own source-generated <see cref="JsonTypeInfo{T}"/> and maps
/// the returned wire object to its result type.
/// </summary>
internal static class ReCaptchaSiteVerifyClient
{
    private static readonly Uri _SiteVerifyUri = new("recaptcha/api/siteverify", UriKind.Relative);

    public static async Task<TResponse> SendAsync<TResponse>(
        HttpClient client,
        string secret,
        CaptchaVerifyRequest request,
        JsonTypeInfo<TResponse> responseTypeInfo,
        ILogger? logger,
        CancellationToken cancellationToken
    )
        where TResponse : class
    {
        List<KeyValuePair<string, string>> formData = [new("secret", secret), new("response", request.Response)];

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
            if (logger?.IsEnabled(LogLevel.Warning) is true)
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

        TResponse? wire;

        try
        {
            wire = await JsonSerializer
                .DeserializeAsync(responseStream, responseTypeInfo, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to deserialize reCAPTCHA siteverify response.", ex);
        }

        return wire
            ?? throw new InvalidOperationException("Failed to deserialize reCAPTCHA response. Response was null.");
    }
}
