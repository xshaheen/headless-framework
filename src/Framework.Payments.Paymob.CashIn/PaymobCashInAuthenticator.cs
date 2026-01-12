// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Framework.Payments.Paymob.CashIn.Models;
using Framework.Payments.Paymob.CashIn.Models.Auth;
using Framework.Urls;
using Microsoft.Extensions.Options;

namespace Framework.Payments.Paymob.CashIn;

public sealed class PaymobCashInAuthenticator : IPaymobCashInAuthenticator, IDisposable
{
    private const string _ClientName = "paymob_cash_in";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptionsMonitor<PaymobCashInOptions> _options;
    private readonly IDisposable? _optionsChangeSubscription;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiration;

    public PaymobCashInAuthenticator(
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        IOptionsMonitor<PaymobCashInOptions> options
    )
    {
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _options = options;

        _optionsChangeSubscription = options.OnChange(_ =>
        {
            _cachedToken = null;
            _tokenExpiration = DateTimeOffset.MinValue;
        });
    }

    public void Dispose()
    {
        _optionsChangeSubscription?.Dispose();
        _tokenLock.Dispose();
    }

    public Task<CashInAuthenticationTokenResponse> RequestAuthenticationTokenAsync() =>
        _RequestAuthenticationTokenAsync(CancellationToken.None);

    private async Task<CashInAuthenticationTokenResponse> _RequestAuthenticationTokenAsync(
        CancellationToken cancellationToken
    )
    {
        var config = _options.CurrentValue;
        var requestUrl = Url.Combine(config.ApiBaseUrl, "auth/tokens");
        var request = new CashInAuthenticationTokenRequest { ApiKey = config.ApiKey };

        var httpClient = _httpClientFactory.CreateClient(_ClientName);
        using var response = await httpClient
            .PostAsJsonAsync(requestUrl, request, config.SerializationOptions, cancellationToken)
            .AnyContext();

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response, default).AnyContext();
        }

        var content = await response
            .Content.ReadFromJsonAsync<CashInAuthenticationTokenResponse>(config.DeserializationOptions)
            .AnyContext();

        if (content is null)
        {
            throw new PaymobCashInException("Paymob CashIn returned null response body.", response.StatusCode, null);
        }

        _cachedToken = content.Token;
        _tokenExpiration = _timeProvider.GetUtcNow().Add(config.TokenRefreshBuffer);

        return content;
    }

    public async ValueTask<string> GetAuthenticationTokenAsync(CancellationToken cancellationToken = default)
    {
        // Fast path - no lock needed for cached valid token
        if (_cachedToken is not null && _tokenExpiration > _timeProvider.GetUtcNow())
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(cancellationToken).AnyContext();
        try
        {
            // Double-check after acquiring lock
            if (_cachedToken is not null && _tokenExpiration > _timeProvider.GetUtcNow())
            {
                return _cachedToken;
            }

            var response = await _RequestAuthenticationTokenAsync(cancellationToken).AnyContext();
            return response.Token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
