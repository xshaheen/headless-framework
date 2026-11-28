// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Headless.Payments.Paymob.CashIn.Internal;
using Headless.Urls;
using Microsoft.Extensions.Options;

namespace Headless.Payments.Paymob.CashIn;

internal sealed class PaymobCashInAuthenticator : IPaymobCashInAuthenticator, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptionsMonitor<PaymobCashInOptions> _options;
    private readonly IDisposable? _optionsChangeSubscription;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    // A single immutable holder swapped atomically (reference assignment), so the lock-free fast path can
    // never observe a torn token/expiration pair (DateTimeOffset writes are not atomic).
    private CachedToken? _cachedToken;

    private sealed record CachedToken(string Token, DateTimeOffset Expiration);

    public PaymobCashInAuthenticator(
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        IOptionsMonitor<PaymobCashInOptions> options
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

    public Task<PaymobCashInResult<CashInAuthenticationTokenResponse>> RequestAuthenticationTokenAsync(
        CancellationToken cancellationToken = default
    )
    {
        return _RequestAuthenticationTokenAsync(cancellationToken);
    }

    private async Task<PaymobCashInResult<CashInAuthenticationTokenResponse>> _RequestAuthenticationTokenAsync(
        CancellationToken cancellationToken
    )
    {
        var config = _options.CurrentValue;
        var requestUrl = Url.Combine(config.ApiBaseUrl, "auth/tokens");
        var request = new CashInAuthenticationTokenRequest { ApiKey = config.ApiKey };

        var httpClient = _httpClientFactory.CreateClient(SetupPaymobCashIn.HttpClientName);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        httpRequest.Content = JsonContent.Create(request, options: CashInJsonOptions.JsonOptions);

        var result = await PaymobCashInHttp
            .SendAsync<CashInAuthenticationTokenResponse>(httpClient, httpRequest, classify: null, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSucceeded)
        {
            _cachedToken = new CachedToken(
                result.Value.Token,
                _timeProvider.GetUtcNow().Add(config.TokenRefreshBuffer)
            );
        }

        return result;
    }

    public async ValueTask<PaymobCashInResult<string>> GetAuthenticationTokenAsync(
        CancellationToken cancellationToken = default
    )
    {
        // Fast path - no lock needed for cached valid token
        var cached = _cachedToken;

        if (cached is not null && cached.Expiration > _timeProvider.GetUtcNow())
        {
            return PaymobCashInResult.Succeeded(cached.Token);
        }

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            cached = _cachedToken;

            if (cached is not null && cached.Expiration > _timeProvider.GetUtcNow())
            {
                return PaymobCashInResult.Succeeded(cached.Token);
            }

            var response = await _RequestAuthenticationTokenAsync(cancellationToken).ConfigureAwait(false);

            return response.Map(static content => content.Token);
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
