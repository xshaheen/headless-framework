// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Recaptcha.Contracts;
using Framework.Recaptcha.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Recaptcha.V3;

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

public sealed class ReCaptchaSiteVerifyV3 : IReCaptchaSiteVerifyV3
{
    private readonly Uri _siteVerifyUri = new("recaptcha/api/siteverify", UriKind.Relative);

    private readonly ReCaptchaOptions _options;
    private readonly HttpClient _client;
    private readonly ILogger<ReCaptchaSiteVerifyV3> _logger;

    public ReCaptchaSiteVerifyV3(
        IOptionsSnapshot<ReCaptchaOptions> optionsAccessor,
        IHttpClientFactory clientFactory,
        ILogger<ReCaptchaSiteVerifyV3> logger
    )
    {
        _options = optionsAccessor.Get(ReCaptchaSetup.V3Name);
        _client = clientFactory.CreateClient(ReCaptchaSetup.V3Name);
        _logger = logger;
    }

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
            .AnyContext();

        if (!httpResponseMessage.IsSuccessStatusCode)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Recaptcha verification failed with status code {StatusCode} and response {Response}",
                    httpResponseMessage.StatusCode,
                    await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken).AnyContext()
                );
            }

            httpResponseMessage.EnsureSuccessStatusCode();
        }

        await using var responseStream = await httpResponseMessage.Content
            .ReadAsStreamAsync(cancellationToken)
            .AnyContext();

        var response = await JsonSerializer.DeserializeAsync(
            utf8Json: responseStream,
            jsonTypeInfo: ReCaptchaJsonSerializerContext.Default.ReCaptchaSiteVerifyV3Response,
            cancellationToken: cancellationToken
        ).AnyContext();

        if (response?.Success is not true)
        {
            _logger.LogReCaptchaFailure(response);
        }

        return response ?? throw new InvalidOperationException(
            "Failed to deserialize reCAPTCHA response. Response was null.");
    }
}
