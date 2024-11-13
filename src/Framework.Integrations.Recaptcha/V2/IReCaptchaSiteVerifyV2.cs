// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Framework.Integrations.Recaptcha.Contracts;
using Framework.Integrations.Recaptcha.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Integrations.Recaptcha.V2;

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
    /// <exception cref="HttpRequestException">The HTTP response is unsuccessful.</exception>
    [SystemPure]
    [JetBrainsPure]
    Task<ReCaptchaSiteVerifyV2Response> Verify(ReCaptchaSiteVerifyRequest request);
}

public sealed class ReCaptchaSiteVerifyV2 : IReCaptchaSiteVerifyV2
{
    private readonly Uri _siteVerifyUri = new("recaptcha/api/siteverify", UriKind.Relative);

    private readonly HttpClient _client;
    private readonly ReCaptchaSettings _settings;
    private readonly ILogger<ReCaptchaSiteVerifyV2> _logger;

    public ReCaptchaSiteVerifyV2(
        IOptionsSnapshot<ReCaptchaSettings> optionsAccessor,
        IHttpClientFactory clientFactory,
        ILogger<ReCaptchaSiteVerifyV2> logger
    )
    {
        _settings = optionsAccessor.Get(ReCaptchaConstants.V2);
        _client = clientFactory.CreateClient(ReCaptchaConstants.V2);
        _client.BaseAddress = new Uri(_settings.VerifyBaseUrl);
        _logger = logger;
    }

    public async Task<ReCaptchaSiteVerifyV2Response> Verify(ReCaptchaSiteVerifyRequest request)
    {
        List<KeyValuePair<string, string>> formData =
        [
            new("secret", _settings.SiteSecret),
            new("response", request.Response),
        ];

        if (request.RemoteIp is not null)
        {
            formData.Add(new("remoteip", request.RemoteIp));
        }

        using var content = new FormUrlEncodedContent(formData);
        using var httpResponseMessage = await _client.PostAsync(_siteVerifyUri, content);

        if (!httpResponseMessage.IsSuccessStatusCode)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Recaptcha verification failed with status code {StatusCode} and response {Response}",
                    httpResponseMessage.StatusCode,
                    await httpResponseMessage.Content.ReadAsStringAsync()
                );
            }

            httpResponseMessage.EnsureSuccessStatusCode();
        }

        await using var responseStream = await httpResponseMessage.Content.ReadAsStreamAsync();

        var response = await JsonSerializer.DeserializeAsync(
            utf8Json: responseStream,
            jsonTypeInfo: ReCaptchaJsonSerializerContext.Default.ReCaptchaSiteVerifyV2Response
        );

        if (response?.Success is not true)
        {
            _logger.LogReCaptchaFailure(response);
        }

        return response!;
    }
}
