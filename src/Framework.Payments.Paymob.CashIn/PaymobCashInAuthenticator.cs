// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Framework.Payments.Paymob.CashIn.Models;
using Framework.Payments.Paymob.CashIn.Models.Auth;
using Framework.Urls;
using Microsoft.Extensions.Options;

namespace Framework.Payments.Paymob.CashIn;

public sealed class PaymobCashInAuthenticator : IPaymobCashInAuthenticator
{
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly IOptionsMonitor<PaymobCashInOptions> _options;
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

    public async Task<CashInAuthenticationTokenResponse> RequestAuthenticationTokenAsync()
    {
        var config = _options.CurrentValue;
        var requestUrl = Url.Combine(config.ApiBaseUrl, "auth/tokens");
        var request = new CashInAuthenticationTokenRequest { ApiKey = config.ApiKey };
        using var response = await _httpClient.PostAsJsonAsync(requestUrl, request, config.SerializationOptions);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response);
        }

        var content = await response.Content.ReadFromJsonAsync<CashInAuthenticationTokenResponse>(
            config.DeserializationOptions
        );

        _cachedToken = content!.Token;
        _tokenExpiration = _timeProvider.GetUtcNow().AddMinutes(55);

        return content;
    }

    public async ValueTask<string> GetAuthenticationTokenAsync()
    {
        if (_cachedToken is not null && _tokenExpiration > _timeProvider.GetUtcNow())
        {
            return _cachedToken;
        }

        var response = await RequestAuthenticationTokenAsync();

        return response.Token;
    }
}
