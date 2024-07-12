using System.Text.Json;
using Framework.Integrations.Recaptcha.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Integrations.Recaptcha;

using JetBrainsPure = PureAttribute;
using SystemPure = System.Diagnostics.Contracts.PureAttribute;

public interface IRecaptchaV2Service
{
    /// <summary>Validate Recapture token.</summary>
    /// <param name="token">The user response token provided by the reCAPTCHA client-side integration onyour site.</param>
    /// <param name="userIp">(Optional) The user's IP address.</param>
    /// <exception cref="HttpRequestException">The HTTP response is unsuccessful.</exception>
    [SystemPure, JetBrainsPure]
    ValueTask<RecaptchaV2Response> ValidateAsync(string token, string? userIp = null);
}

/// <summary><a href="https://developers.google.com/recaptcha/docs/verify">API Documentation</a>.</summary>
[PublicAPI]
internal sealed class RecaptchaV2Service(
    HttpClient httpClient,
    IOptions<RecaptchaSettings> options,
    ILogger<RecaptchaV2Service> logger
) : IRecaptchaV2Service
{
    private readonly RecaptchaSettings _settings = options.Value;
    private readonly Uri _siteVerifyUri = new("recaptcha/api/siteverify", UriKind.Relative);

    public async ValueTask<RecaptchaV2Response> ValidateAsync(string token, string? userIp = null)
    {
        var formData = new List<KeyValuePair<string?, string?>>
        {
            new("secret", _settings.SecretKey),
            new("response", token),
        };

        if (userIp is not null)
        {
            formData.Add(new KeyValuePair<string?, string?>("remoteip", userIp));
        }

        using var content = new FormUrlEncodedContent(formData);
        using var response = await httpClient.PostAsync(_siteVerifyUri, content);

        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync();

        var captchaResponse = await JsonSerializer.DeserializeAsync(
            utf8Json: responseStream,
            jsonTypeInfo: InternalRecaptchaV2ResponseContext.Default.InternalRecaptchaV2Response
        );

        if (!captchaResponse!.Success)
        {
            logger.LogRecaptchaFailure(captchaResponse);
        }

        return new RecaptchaV2Response
        {
            Success = captchaResponse.Success,
            Hostname = captchaResponse.Hostname,
            ChallengeTimeStamp = captchaResponse.ChallengeTimeStamp,
            ErrorCodes = captchaResponse.ErrorCodes.Select(_ConvertResponseError).ToArray(),
        };
    }

    private static RecaptchaV2Error _ConvertResponseError(string error)
    {
        return error switch
        {
            "bad-request" => RecaptchaV2Error.BadRequest,
            "timeout-or-duplicate" => RecaptchaV2Error.TimeOutOrDuplicate,
            "invalid-input-response" => RecaptchaV2Error.InvalidInputResponse,
            "missing-input-response" => RecaptchaV2Error.MissingInputResponse,
            "invalid-input-secret" => RecaptchaV2Error.InvalidInputSecret,
            "missing-input-secret" => RecaptchaV2Error.MissingInputSecret,
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, @"Unexpected recaptcha error"),
        };
    }
}
