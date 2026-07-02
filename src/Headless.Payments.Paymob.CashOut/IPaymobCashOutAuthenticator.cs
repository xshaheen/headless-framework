// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Headless.Http;
using Headless.Payments.Paymob.CashOut.Internals;
using Headless.Payments.Paymob.CashOut.Models;
using Headless.Urls;
using Microsoft.Extensions.Options;

namespace Headless.Payments.Paymob.CashOut;

/// <summary>
/// Handles OAuth 2.0 password-grant authentication against the Paymob CashOut API.
/// </summary>
/// <remarks>
/// <para>
/// The implementation obtains an access token by posting the configured username and password
/// with a Basic-auth header (client ID/secret). The token is cached in memory and renewed
/// proactively before expiry using <c>PaymobCashOutOptions.TokenRefreshBuffer</c> (default 10
/// minutes). Concurrent callers during a refresh are serialised through a semaphore.
/// </para>
/// <para>
/// Option changes (via <c>IOptionsMonitor</c>) automatically invalidate the cached token, so the
/// next call fetches a fresh one with the updated credentials.
/// </para>
/// <para>Register with <c>SetupPaymobCashOut.AddPaymobCashOut</c>, which registers this as a singleton.</para>
/// </remarks>
public interface IPaymobCashOutAuthenticator
{
    /// <summary>
    /// Returns a valid OAuth access token, using a cached value when it has not yet expired.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the network call if a refresh is needed.</param>
    /// <returns>A bearer access token string for use in Paymob CashOut API requests.</returns>
    /// <remarks>
    /// The fast path (valid cached token) avoids all locking overhead. When the cached token is
    /// expired or absent, a double-checked lock ensures only one refresh call is made.
    /// </remarks>
    ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a refresh token for a new access token using the OAuth refresh_token grant.
    /// </summary>
    /// <param name="refreshToken">The refresh token received from a previous authentication response.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A new authentication response containing a fresh access token, refresh token, and expiry.
    /// The new access token is also stored in the internal cache.
    /// </returns>
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

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_cachedToken is not null && _tokenExpiration > _timeProvider.GetUtcNow())
            {
                return _cachedToken;
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

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

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
        var httpClient = _httpClientFactory.CreateClient(SetupPaymobCashOut.HttpClientName);

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri(requestUrl, UriKind.Absolute);
        request.Headers.Authorization = AuthenticationHeaderFactory.CreateBasic(options.ClientId, options.ClientSecret);
        request.Content = new FormUrlEncodedContent([
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken),
        ]);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

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
        _cachedToken = content.AccessToken;
        _tokenExpiration = _timeProvider.GetUtcNow().Add(options.TokenRefreshBuffer);

        return content;
    }
}
