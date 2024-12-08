// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Sms.Cequens;

public sealed class CequensSmsSender(
    HttpClient httpClient,
    IOptions<CequensOptions> optionsAccessor,
    ILogger<CequensSmsSender> logger
) : ISmsSender
{
    private readonly CequensOptions _options = optionsAccessor.Value;

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken token = default
    )
    {
        httpClient.BaseAddress = new Uri(_options.Uri);
        var jwtToken = await _GetTokenRequest(token) ?? _options.Token;
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);

        var apiRequest = new
        {
            senderName = _options.SenderName,
            messageType = "text",
            acknowledgement = 0,
            flashing = 0,
            messageText = request.Text,
            recipients = request.Destination.ToString(),
        };

        var response = await httpClient.PostAsJsonAsync("sms/v1/messages", apiRequest, token);
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

    private async Task<string?> _GetTokenRequest(CancellationToken cancellationToken)
    {
        var request = new { apiKey = _options.ApiKey, userName = _options.UserName };
        var response = await httpClient.PostAsJsonAsync("auth/v1/tokens", request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = string.IsNullOrEmpty(content) ? "Failed to get token from Cequens API" : content;
            logger.LogError("Failed to get token from Cequens API: {StatusCode}, {Body}", response.StatusCode, error);

            return null;
        }

        var token = JsonSerializer.Deserialize<CequensAuthResponse>(content);

        return token?.Data?.AccessToken;
    }

    [UsedImplicitly]
    private sealed class CequensAuthResponse
    {
        public DataResponse? Data { get; init; }

        public sealed class DataResponse
        {
            [JsonPropertyName("access_token")]
            public required string AccessToken { get; init; }
        }
    }

    #endregion
}
