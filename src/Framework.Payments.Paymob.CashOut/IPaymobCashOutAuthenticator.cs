// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Framework.Http;
using Framework.Payments.Paymob.CashOut.Internals;
using Framework.Payments.Paymob.CashOut.Models;
using Framework.Urls;
using Microsoft.Extensions.Options;

namespace Framework.Payments.Paymob.CashOut;

public interface IPaymobCashOutAuthenticator
{
    ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    Task<CashOutAuthenticationResponse> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default
    );
}

public sealed class PaymobCashOutAuthenticator : IPaymobCashOutAuthenticator, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptionsMonitor<PaymobCashOutOptions> _options;
    private readonly IDisposable? _optionsChangeSubscription;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiration;

    public PaymobCashOutAuthenticator(
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        IOptionsMonitor<PaymobCashOutOptions> options
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

    public async ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
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

            var response = await _GenerateTokenAsync(cancellationToken).AnyContext();
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
        var httpClient = _httpClientFactory.CreateClient(PaymobCashOutSetup.HttpClientName);

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri(Url.Combine(options.ApiBaseUrl, "o/token/"), UriKind.Absolute);
        request.Content = new FormUrlEncodedContent([
            new("grant_type", "password"),
            new("username", options.UserName),
            new("password", options.Password),
        ]);
        request.Headers.Authorization = AuthenticationHeaderFactory.CreateBasic(options.ClientId, options.ClientSecret);

        var response = await httpClient.SendAsync(request, cancellationToken).AnyContext();

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response).AnyContext();
        }

        var content = (
            await response
                .Content.ReadFromJsonAsync<CashOutAuthenticationResponse>(
                    CashOutJsonOptions.JsonOptions,
                    cancellationToken
                )
                .AnyContext()
        )!;

        _cachedToken = content.AccessToken;
        _tokenExpiration = _timeProvider.GetUtcNow().Add(options.TokenRefreshBuffer);

        return content;
    }

    public async Task<CashOutAuthenticationResponse> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default
    )
    {
        var options = _options.CurrentValue;
        var requestUrl = Url.Combine(options.ApiBaseUrl, "o/token");
        var httpClient = _httpClientFactory.CreateClient(PaymobCashOutSetup.HttpClientName);

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri(requestUrl, UriKind.Absolute);
        request.Headers.Authorization = AuthenticationHeaderFactory.CreateBasic(options.ClientId, options.ClientSecret);
        request.Content = new FormUrlEncodedContent([
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken),
        ]);

        var response = await httpClient.SendAsync(request, cancellationToken).AnyContext();

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response).AnyContext();
        }

        var content = (
            await response
                .Content.ReadFromJsonAsync<CashOutAuthenticationResponse>(
                    CashOutJsonOptions.JsonOptions,
                    cancellationToken
                )
                .AnyContext()
        )!;

        // Update cache with refreshed token
        _cachedToken = content.AccessToken;
        _tokenExpiration = _timeProvider.GetUtcNow().Add(options.TokenRefreshBuffer);

        return content;
    }
}
