// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Framework.Payments.Paymob.CashIn.Models;
using Framework.Payments.Paymob.CashIn.Models.Auth;
using Framework.Urls;
using Microsoft.Extensions.Options;

namespace Framework.Payments.Paymob.CashIn;

public sealed class PaymobCashInAuthenticator : IPaymobCashInAuthenticator, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly IOptionsMonitor<PaymobCashInOptions> _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiration;

    public PaymobCashInAuthenticator(
        HttpClient httpClient,
        TimeProvider timeProvider,
        IOptionsMonitor<PaymobCashInOptions> options
    )
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider;
        _options = options;

        options.OnChange(_ =>
        {
            _cachedToken = null;
            _tokenExpiration = DateTimeOffset.MinValue;
        });
    }

    public void Dispose() => _tokenLock.Dispose();

    public async Task<CashInAuthenticationTokenResponse> RequestAuthenticationTokenAsync()
    {
        var config = _options.CurrentValue;
        var requestUrl = Url.Combine(config.ApiBaseUrl, "auth/tokens");
        var request = new CashInAuthenticationTokenRequest { ApiKey = config.ApiKey };
        using var response = await _httpClient
            .PostAsJsonAsync(requestUrl, request, config.SerializationOptions)
            .AnyContext();

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response).AnyContext();
        }

        var content = await response
            .Content.ReadFromJsonAsync<CashInAuthenticationTokenResponse>(config.DeserializationOptions)
            .AnyContext();

        if (content is null)
        {
            throw new PaymobCashInException("Paymob CashIn returned null response body.", response.StatusCode, null);
        }

        _cachedToken = content.Token;
        _tokenExpiration = _timeProvider.GetUtcNow().AddMinutes(55);

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

            var response = await RequestAuthenticationTokenAsync().AnyContext();
            return response.Token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
