// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Http;
using Headless.Payments.Paymob.CashOut.Internal;
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
[PublicAPI]
public interface IPaymobCashOutAuthenticator
{
    /// <summary>
    /// Returns a valid OAuth access token, using a cached value when it has not yet expired.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the network call if a refresh is needed.</param>
    /// <returns>
    /// The bearer access token on <c>Succeeded</c>. <c>Rejected</c> means Paymob refused the credentials
    /// (4xx); <c>Unknown</c> means the token endpoint gave no usable answer. A token request moves no money,
    /// so either is safe to retry.
    /// </returns>
    /// <remarks>
    /// The fast path (valid cached token) avoids all locking overhead. When the cached token is
    /// expired or absent, a double-checked lock ensures only one refresh call is made. A failed
    /// request caches nothing.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the request was sent.</exception>
    ValueTask<PaymobCashOutResult<string>> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a refresh token for a new access token using the OAuth refresh_token grant.
    /// </summary>
    /// <param name="refreshToken">The refresh token received from a previous authentication response.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// On <c>Succeeded</c>, a new authentication response containing a fresh access token, refresh token,
    /// and expiry; the new access token is also stored in the internal cache. <c>Rejected</c> or
    /// <c>Unknown</c> leaves the cache unchanged.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="refreshToken"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the request was sent.</exception>
    Task<PaymobCashOutResult<CashOutAuthenticationResponse>> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default
    );
}

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

    public async ValueTask<PaymobCashOutResult<string>> GetAccessTokenAsync(
        CancellationToken cancellationToken = default
    )
    {
        // Fast path - no lock needed for cached valid token
        var cached = _cachedToken;

        if (cached is not null && cached.Expiration > _timeProvider.GetUtcNow())
        {
            return PaymobCashOutResult.Succeeded(cached.Token);
        }

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            cached = _cachedToken;

            if (cached is not null && cached.Expiration > _timeProvider.GetUtcNow())
            {
                return PaymobCashOutResult.Succeeded(cached.Token);
            }

            var options = _options.CurrentValue;

            var response = await _SendTokenRequestAsync(
                    options,
                    "o/token/",
                    [
                        new("grant_type", "password"),
                        new("username", options.UserName),
                        new("password", options.Password),
                    ],
                    cancellationToken
                )
                .ConfigureAwait(false);

            return response.Map(content => content.AccessToken);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public Task<PaymobCashOutResult<CashOutAuthenticationResponse>> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(refreshToken);

        return _SendTokenRequestAsync(
            _options.CurrentValue,
            "o/token",
            [new("grant_type", "refresh_token"), new("refresh_token", refreshToken)],
            cancellationToken
        );
    }

    private async Task<PaymobCashOutResult<CashOutAuthenticationResponse>> _SendTokenRequestAsync(
        PaymobCashOutOptions options,
        string path,
        KeyValuePair<string, string>[] form,
        CancellationToken cancellationToken
    )
    {
        var httpClient = _httpClientFactory.CreateClient(SetupPaymobCashOut.HttpClientName);

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri(Url.Combine(options.ApiBaseUrl, path), UriKind.Absolute);
        request.Content = new FormUrlEncodedContent(form);
        request.Headers.Authorization = AuthenticationHeaderFactory.CreateBasic(options.ClientId, options.ClientSecret);

        var result = await PaymobCashOutHttp
            .SendAsync<CashOutAuthenticationResponse>(httpClient, request, classify: null, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSucceeded)
        {
            _cachedToken = new CachedToken(
                result.Value.AccessToken,
                _timeProvider.GetUtcNow().Add(options.TokenRefreshBuffer)
            );
        }

        return result;
    }
}
