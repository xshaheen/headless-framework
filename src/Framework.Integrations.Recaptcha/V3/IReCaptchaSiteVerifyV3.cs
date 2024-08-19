using System.Text.Json;
using Framework.Integrations.Recaptcha.Contracts;
using Framework.Integrations.Recaptcha.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Integrations.Recaptcha.V3;

public interface IReCaptchaSiteVerifyV3
{
    Task<ReCaptchaSiteVerifyV3Response> Verify(ReCaptchaSiteVerifyRequest request);
}

public sealed class ReCaptchaSiteVerifyV3 : IReCaptchaSiteVerifyV3
{
    private readonly Uri _siteVerifyUri = new("recaptcha/api/siteverify", UriKind.Relative);

    private readonly ReCaptchaSettings _settings;
    private readonly HttpClient _client;
    private readonly ILogger<ReCaptchaSiteVerifyV3> _logger;

    public ReCaptchaSiteVerifyV3(
        IOptionsSnapshot<ReCaptchaSettings> optionsAccessor,
        IHttpClientFactory clientFactory,
        ILogger<ReCaptchaSiteVerifyV3> logger
    )
    {
        _settings = optionsAccessor.Get(ReCaptchaConstants.V3);
        _client = clientFactory.CreateClient(ReCaptchaConstants.V3);
        _client.BaseAddress = new Uri(_settings.VerifyBaseUrl);
        _logger = logger;
    }

    public async Task<ReCaptchaSiteVerifyV3Response> Verify(ReCaptchaSiteVerifyRequest request)
    {
        IEnumerable<KeyValuePair<string, string?>> formData =
        [
            new("secret", _settings.SiteSecret),
            new("response", request.Response),
            new("remoteip", request.RemoteIp),
        ];

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
            jsonTypeInfo: ReCaptchaJsonSerializerContext.Default.ReCaptchaSiteVerifyV3Response
        );

        if (response?.Success is not true)
        {
            _logger.LogReCaptchaFailure(response);
        }

        return response!;
    }
}
