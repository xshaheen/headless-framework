// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Headless.Http;
using Headless.Payments.Paymob.CashOut.Internal;
using Headless.Payments.Paymob.CashOut.Models;
using Headless.Urls;
using Microsoft.Extensions.Options;

namespace Headless.Payments.Paymob.CashOut;

internal sealed class PaymobCashOutAuthenticator : IPaymobCashOutAuthenticator, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptionsMonitor<PaymobCashOutOptions> _options;
    private readonly IDisposable? _optionsChangeSubscription;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    // A single immutable holder swapped atomically (reference assignment), so the lock-free fast path can
    // never observe a torn token/expiration pair (DateTimeOffset writes are not atomic).
    private CachedToken? _cachedToken;

    private sealed record CachedToken(string Token, DateTimeOffset Expiration);

    public PaymobCashOutAuthenticator(
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        IOptionsMonitor<PaymobCashOutOptions> options
    )
    {
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _options = options;

        _optionsChangeSubscription = options.OnChange(_ => _cachedToken = null);
    }

    public void Dispose()
    {
        _optionsChangeSubscription?.Dispose();
        _tokenLock.Dispose();
    }

    public async ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // Fast path - no lock needed for cached valid token
        var cached = _cachedToken;

        if (cached is not null && cached.Expiration > _timeProvider.GetUtcNow())
        {
            return cached.Token;
        }

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            cached = _cachedToken;

            if (cached is not null && cached.Expiration > _timeProvider.GetUtcNow())
            {
                return cached.Token;
            }

            var response = await _GenerateTokenAsync(cancellationToken).ConfigureAwait(false);
            return response.AccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<CashOutAuthenticationResponse> _GenerateTokenAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var httpClient = _httpClientFactory.CreateClient(SetupPaymobCashOut.HttpClientName);

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri(Url.Combine(options.ApiBaseUrl, "o/token/"), UriKind.Absolute);
        request.Content = new FormUrlEncodedContent([
            new("grant_type", "password"),
            new("username", options.UserName),
            new("password", options.Password),
        ]);
        request.Headers.Authorization = AuthenticationHeaderFactory.CreateBasic(options.ClientId, options.ClientSecret);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response, cancellationToken).ConfigureAwait(false);
        }

        var content = (
            await response
                .Content.ReadFromJsonAsync<CashOutAuthenticationResponse>(
                    CashOutJsonOptions.JsonOptions,
                    cancellationToken
                )
                .ConfigureAwait(false)
        )!;

        _cachedToken = new CachedToken(content.AccessToken, _timeProvider.GetUtcNow().Add(options.TokenRefreshBuffer));

        return content;
    }

    public async Task<CashOutAuthenticationResponse> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default
    )
    {
        var options = _options.CurrentValue;
        var requestUrl = Url.Combine(options.ApiBaseUrl, "o/token");
        var httpClient = _httpClientFactory.CreateClient(SetupPaymobCashOut.HttpClientName);

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri(requestUrl, UriKind.Absolute);
        request.Headers.Authorization = AuthenticationHeaderFactory.CreateBasic(options.ClientId, options.ClientSecret);
        request.Content = new FormUrlEncodedContent([
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken),
        ]);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response, cancellationToken).ConfigureAwait(false);
        }

        var content = (
            await response
                .Content.ReadFromJsonAsync<CashOutAuthenticationResponse>(
                    CashOutJsonOptions.JsonOptions,
                    cancellationToken
                )
                .ConfigureAwait(false)
        )!;

        // Update cache with refreshed token
        _cachedToken = new CachedToken(content.AccessToken, _timeProvider.GetUtcNow().Add(options.TokenRefreshBuffer));

        return content;
    }
}
