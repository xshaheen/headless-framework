// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
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
    string httpClientName,
    TimeProvider timeProvider,
    IOptionsMonitor<CequensSmsOptions> optionsMonitor,
    string? optionsName,
    ILogger<CequensSmsSender> logger
) : ISmsSender, IBulkSmsSender, IDisposable
{
    private static readonly JsonSerializerOptions _JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = CequensJsonSerializerContext.Default,
    };

    // Snapshot for this instance's options name — never CurrentValue, which binds the default options and
    // would bleed configuration across keyed instances.
    private readonly CequensSmsOptions _options = optionsMonitor.Get(optionsName);
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(request.Destination);
        Argument.IsNotEmpty(request.Text);

        return await _SendAsync(
                request.Destination.ToString(),
                request.MessageId,
                request.Text,
                destinationCount: 1,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask<SendBulkSmsResponse> SendBulkAsync(
        SendBulkSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(request.Destinations);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        // Cequens accepts a comma-separated recipient list and reports a single status, so the same outcome
        // applies to every recipient.
        var outcome = await _SendAsync(
                string.Join(',', request.Destinations),
                request.MessageId,
                request.Text,
                request.Destinations.Count,
                cancellationToken
            )
            .ConfigureAwait(false);

        return SendBulkSmsResponse.FromAggregate(request.Destinations, outcome);
    }

    private async ValueTask<SendSingleSmsResponse> _SendAsync(
        string recipients,
        string? messageId,
        string text,
        int destinationCount,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await _SendCoreAsync(recipients, messageId, text, destinationCount, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogSmsSendException(e, destinationCount);

            return SendSingleSmsResponse.FromException(e);
        }
    }

    private async ValueTask<SendSingleSmsResponse> _SendCoreAsync(
        string recipients,
        string? messageId,
        string text,
        int destinationCount,
        CancellationToken cancellationToken
    )
    {
        using var httpClient = httpClientFactory.CreateClient(httpClientName);

        var apiRequest = new SendSmsRequest
        {
            ClientMessageId =
                messageId is not null && int.TryParse(messageId, CultureInfo.InvariantCulture, out var id) ? id : null,
            SenderName = _options.SenderName,
            MessageText = text,
            Recipients = recipients,
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
                logger.LogSmsSentSuccessfully(destinationCount, response.StatusCode);

                return SendSingleSmsResponse.Succeeded();
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _InvalidateToken();

                continue;
            }

            logger.LogFailedToSendSms(destinationCount, response.StatusCode);

            var rawContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var error = string.IsNullOrEmpty(rawContent) ? "Failed to send SMS using Cequens API" : rawContent;

            // Cequens publishes no machine-readable error contract for this endpoint; the only status with
            // unambiguous meaning is the 401 the re-auth path above already keys on (bearer token rejected).
            // Everything else surfaces the raw body without guessing a kind.
            var kind =
                response.StatusCode is HttpStatusCode.Unauthorized
                    ? SmsFailureKind.AuthFailure
                    : SmsFailureKind.Unknown;

            return SendSingleSmsResponse.Failed(error, kind);
        }
    }

    #region Helpers

    // A single immutable holder swapped atomically (reference assignment is atomic), so the fast-path read
    // outside the lock can never observe a torn token/expiration pair.
    private CachedToken? _cached;

    private sealed record CachedToken(string Token, DateTime Expiration);

    private async Task<string?> _GetTokenRequestAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Quick check before lock
        var cached = _cached;
        if (cached is not null && cached.Expiration > now)
        {
            return cached.Token;
        }

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            now = timeProvider.GetUtcNow().UtcDateTime;
            cached = _cached;
            if (cached is not null && cached.Expiration > now)
            {
                return cached.Token;
            }

            try
            {
                var signInRequest = new SigningInRequest(_options.ApiKey, _options.UserName);
                using var signInContent = JsonContent.Create(signInRequest, options: _JsonOptions);
                using var response = await httpClient
                    .PostAsync(_options.TokenEndpoint, signInContent, cancellationToken)
                    .ConfigureAwait(false);

                // Only the status code is reported on failure, so skip buffering the body to a string and
                // deserialize the success payload straight off the response stream.
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogFailedToGetTokenWithStatusCode(response.StatusCode);

                    return null;
                }

                var signInResponse = await response
                    .Content.ReadFromJsonAsync<SigningInResponse>(_JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                var token = signInResponse?.Data?.AccessToken;

                if (token != null)
                {
                    _cached = new CachedToken(token, _ComputeTokenExpiration(token, now));
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
        _cached = null;
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
        return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value));
    }

    public void Dispose()
    {
        _tokenLock.Dispose();
    }

    #endregion
}
