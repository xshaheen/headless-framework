// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
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
internal sealed class CequensSmsSender(
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

        try
        {
            return await _SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogSmsSendException(e, request.Destinations.Count);

            return SendSingleSmsResponse.Failed(e.Message, SmsFailureKind.Transient);
        }
    }

    private async ValueTask<SendSingleSmsResponse> _SendCoreAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken
    )
    {
        using var httpClient = httpClientFactory.CreateClient(SetupCequens.HttpClientName);

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

        // At most two attempts: a 401 invalidates a stale cached token so the retry re-authenticates.
        for (var attempt = 0; ; attempt++)
        {
            var jwtToken =
                await _GetTokenRequestAsync(httpClient, cancellationToken).ConfigureAwait(false) ?? _options.Token;

            if (string.IsNullOrEmpty(jwtToken))
            {
                logger.LogFailedToGetToken();

                return SendSingleSmsResponse.Failed("Failed to get token from Cequens API", SmsFailureKind.AuthFailure);
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.SingleSmsEndpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);
            httpRequest.Content = JsonContent.Create(apiRequest, options: _JsonOptions);

            using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                logger.LogSmsSentSuccessfully(request.Destinations.Count, response.StatusCode);

                return SendSingleSmsResponse.Succeeded();
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _InvalidateToken();

                continue;
            }

            logger.LogFailedToSendSms(request.Destinations.Count, response.StatusCode);

            var rawContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var error = string.IsNullOrEmpty(rawContent) ? "Failed to send SMS using Cequens API" : rawContent;
            var failureKind =
                response.StatusCode == HttpStatusCode.Unauthorized
                    ? SmsFailureKind.AuthFailure
                    : SmsFailureKind.Unknown;

            return SendSingleSmsResponse.Failed(error, failureKind);
        }
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

            try
            {
                var signInRequest = new SigningInRequest(_options.ApiKey, _options.UserName);
                using var signInContent = JsonContent.Create(signInRequest, options: _JsonOptions);
                var response = await httpClient
                    .PostAsync(_options.TokenEndpoint, signInContent, cancellationToken)
                    .ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogFailedToGetTokenWithStatusCode(response.StatusCode);

                    return null;
                }

                var token = JsonSerializer.Deserialize<SigningInResponse>(content, _JsonOptions)?.Data?.AccessToken;

                if (token != null)
                {
                    _cachedToken = token;
                    _tokenExpiration = _ComputeTokenExpiration(token, now);
                }

                return token;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                // Return null so the caller can fall back to a statically configured token.
                logger.LogFailedToGetTokenException(e);

                return null;
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void _InvalidateToken()
    {
        _cachedToken = null;
        _tokenExpiration = default;
    }

    /// <summary>
    /// Caches the token until shortly before the JWT's <c>exp</c> claim, falling back to a conservative
    /// 10-minute window when the token is not a parseable JWT or carries no usable expiry.
    /// </summary>
    private static DateTime _ComputeTokenExpiration(string token, DateTime now)
    {
        var fallback = now.AddMinutes(10);
        var parts = token.Split('.');

        if (parts.Length < 2)
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(_Base64UrlDecode(parts[1]));

            if (document.RootElement.TryGetProperty("exp", out var expElement) && expElement.TryGetInt64(out var exp))
            {
                var refreshAt = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime - TimeSpan.FromSeconds(30);

                return refreshAt > now ? refreshAt : fallback;
            }
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            // Malformed token payload — fall back to the conservative window.
        }

        return fallback;
    }

    private static string _Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');

        normalized += (normalized.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => "",
        };

        return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
    }

    public void Dispose()
    {
        _tokenLock.Dispose();
    }

    #endregion
}
