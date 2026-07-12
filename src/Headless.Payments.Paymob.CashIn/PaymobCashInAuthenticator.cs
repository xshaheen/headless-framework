// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Headless.Payments.Paymob.CashIn.Internals;
using Headless.Payments.Paymob.CashIn.Models;
using Headless.Payments.Paymob.CashIn.Models.Auth;
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

    public Task<CashInAuthenticationTokenResponse> RequestAuthenticationTokenAsync(
        CancellationToken cancellationToken = default
    ) => _RequestAuthenticationTokenAsync(cancellationToken);

    private async Task<CashInAuthenticationTokenResponse> _RequestAuthenticationTokenAsync(
        CancellationToken cancellationToken
    )
    {
        var config = _options.CurrentValue;
        var requestUrl = Url.Combine(config.ApiBaseUrl, "auth/tokens");
        var request = new CashInAuthenticationTokenRequest { ApiKey = config.ApiKey };

        var httpClient = _httpClientFactory.CreateClient(SetupPaymobCashIn.HttpClientName);

        using var response = await httpClient
            .PostAsJsonAsync(requestUrl, request, CashInJsonOptions.JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response, CancellationToken.None).ConfigureAwait(false);
        }

        var content =
            await response
                .Content.ReadFromJsonAsync<CashInAuthenticationTokenResponse>(
                    CashInJsonOptions.JsonOptions,
                    cancellationToken
                )
                .ConfigureAwait(false)
            ?? throw new PaymobCashInException(
                "Paymob CashIn returned null response body.",
                response.StatusCode,
                body: null
            );

        _cachedToken = new CachedToken(content.Token, _timeProvider.GetUtcNow().Add(config.TokenRefreshBuffer));

        return content;
    }

    public async ValueTask<string> GetAuthenticationTokenAsync(CancellationToken cancellationToken = default)
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

            var response = await _RequestAuthenticationTokenAsync(cancellationToken).ConfigureAwait(false);
            return response.Token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
