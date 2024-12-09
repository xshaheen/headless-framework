// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using Framework.Sms.Cequens.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Sms.Cequens;

/*
 * Docs: https://developer.cequens.com/reference/sending-sms
 */
public sealed class CequensSmsSender(
    HttpClient httpClient,
    IOptions<CequensSmsOptions> optionsAccessor,
    ILogger<CequensSmsSender> logger
) : ISmsSender
{
    private readonly CequensSmsOptions _options = optionsAccessor.Value;

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken token = default
    )
    {
        var jwtToken = await _GetTokenRequestAsync(token) ?? _options.Token;

        if (string.IsNullOrEmpty(jwtToken))
        {
            logger.LogError("Failed to get token from Cequens API");

            return SendSingleSmsResponse.Failed("Failed to get token from Cequens API");
        }

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);

        var apiRequest = new SendSmsRequest
        {
            ClientMessageId =
                request.MessageId is not null
                && int.TryParse(request.MessageId, CultureInfo.InvariantCulture, out var id)
                    ? id
                    : null,
            SenderName = _options.SenderName,
            MessageText = request.Text,
            Recipients = request.IsBatch ? string.Join(',', request.Destinations) : request.Destinations[0].ToString(),
        };

        var response = await httpClient.PostAsJsonAsync(_options.SingleSmsEndpoint, apiRequest, token);
        var rawContent = await response.Content.ReadAsStringAsync(token);

        if (response.IsSuccessStatusCode)
        {
            logger.LogTrace(
                "SMS sent successfully using Cequens API: {StatusCode}, {Body}",
                response.StatusCode,
                rawContent
            );

            return SendSingleSmsResponse.Succeeded();
        }

        var error = string.IsNullOrEmpty(rawContent) ? "Failed to send SMS using Cequens API" : rawContent;
        logger.LogError("Failed to send SMS using Cequens API: {StatusCode}, {Body}", response.StatusCode, error);

        return SendSingleSmsResponse.Failed(error);
    }

    #region Helpers

    private string? _cachedToken;
    private DateTime _tokenExpiration;

    private async Task<string?> _GetTokenRequestAsync(CancellationToken cancellationToken)
    {
        if (_cachedToken != null && _tokenExpiration > DateTime.UtcNow)
        {
            return _cachedToken;
        }

        var request = new SigningInRequest(_options.ApiKey, _options.UserName);
        var response = await httpClient.PostAsJsonAsync(_options.TokenEndpoint, request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "Failed to get token from Cequens API: {StatusCode}, {Body}",
                response.StatusCode,
                response
            );

            return null;
        }

        var token = JsonSerializer.Deserialize<SigningInResponse>(content)?.Data?.AccessToken;

        if (token != null)
        {
            _cachedToken = token;
            _tokenExpiration = DateTime.UtcNow.AddMinutes(10);
        }

        return token;
    }

    #endregion
}
