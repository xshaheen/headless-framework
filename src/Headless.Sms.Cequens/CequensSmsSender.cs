// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using Headless.Checks;
using Headless.Sms.Cequens.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Sms.Cequens;

/*
 * Docs: https://developer.cequens.com/reference/sending-sms
 */
public sealed class CequensSmsSender(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    IOptions<CequensSmsOptions> optionsAccessor,
    ILogger<CequensSmsSender> logger
) : ISmsSender, IDisposable
{
    private static readonly JsonSerializerOptions _JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = CequensJsonSerializerContext.Default,
    };

    private readonly CequensSmsOptions _options = optionsAccessor.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        using var httpClient = httpClientFactory.CreateClient(CequensSetup.HttpClientName);

        var jwtToken =
            await _GetTokenRequestAsync(httpClient, cancellationToken).ConfigureAwait(false) ?? _options.Token;

        if (string.IsNullOrEmpty(jwtToken))
        {
            logger.LogError("Failed to get token from Cequens API");

            return SendSingleSmsResponse.Failed("Failed to get token from Cequens API");
        }

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

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.SingleSmsEndpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);
        httpRequest.Content = JsonContent.Create(apiRequest, options: _JsonOptions);

        var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            logger.LogTrace(
                "SMS sent successfully using Cequens API to {DestinationCount} recipients, StatusCode={StatusCode}",
                request.Destinations.Count,
                response.StatusCode
            );

            return SendSingleSmsResponse.Succeeded();
        }

        logger.LogError(
            "Failed to send SMS using Cequens API to {DestinationCount} recipients, StatusCode={StatusCode}",
            request.Destinations.Count,
            response.StatusCode
        );

        var error = string.IsNullOrEmpty(rawContent) ? "Failed to send SMS using Cequens API" : rawContent;

        return SendSingleSmsResponse.Failed(error);
    }

    #region Helpers

    private string? _cachedToken;
    private DateTime _tokenExpiration;

    private async Task<string?> _GetTokenRequestAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Quick check before lock
        if (_cachedToken != null && _tokenExpiration > now)
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            now = timeProvider.GetUtcNow().UtcDateTime;
            if (_cachedToken != null && _tokenExpiration > now)
            {
                return _cachedToken;
            }

            var signInRequest = new SigningInRequest(_options.ApiKey, _options.UserName);
            using var signInContent = JsonContent.Create(signInRequest, options: _JsonOptions);
            var response = await httpClient
                .PostAsync(_options.TokenEndpoint, signInContent, cancellationToken)
                .ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Failed to get token from Cequens API, StatusCode={StatusCode}", response.StatusCode);

                return null;
            }

            var token = JsonSerializer.Deserialize<SigningInResponse>(content, _JsonOptions)?.Data?.AccessToken;

            if (token != null)
            {
                _cachedToken = token;
                _tokenExpiration = now.AddMinutes(10);
            }

            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public void Dispose()
    {
        _tokenLock.Dispose();
    }

    #endregion
}
