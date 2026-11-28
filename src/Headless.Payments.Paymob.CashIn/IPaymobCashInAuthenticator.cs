// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn;

/// <summary>
/// Handles authentication against the Paymob Accept API using the legacy API-key flow.
/// </summary>
/// <remarks>
/// Paymob issues a short-lived bearer token in exchange for the merchant's API key. The token is
/// valid for 60 minutes. <c>GetAuthenticationTokenAsync</c> caches the token in memory and renews it
/// proactively based on <c>TokenRefreshBuffer</c> in <c>PaymobCashInOptions</c>; concurrent callers are
/// serialised through a semaphore so that only one refresh request is in flight at a time.
/// </remarks>
[PublicAPI]
public interface IPaymobCashInAuthenticator
{
    /// <summary>Exchanges the configured API key for a new authentication token unconditionally.</summary>
    /// <param name="cancellationToken">Token to cancel the network call.</param>
    /// <returns>
    /// A succeeded result with the bearer token and merchant profile, or a rejected or unknown result with the
    /// status and body Paymob answered with. A new token is cached only on success.
    /// </returns>
    /// <exception cref="OperationCanceledException">The token was canceled before the request was sent.</exception>
    Task<PaymobCashInResult<CashInAuthenticationTokenResponse>> RequestAuthenticationTokenAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns a valid authentication token, using a cached value when it has not yet expired.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the network call if a refresh is needed.</param>
    /// <returns>
    /// A succeeded result with a bearer token that is valid for Paymob API requests. The cached token is
    /// returned when it remains valid; otherwise a fresh token is fetched and cached. A failed refresh returns
    /// its rejected or unknown result and caches nothing.
    /// </returns>
    /// <remarks>
    /// Token lifetime is controlled by <c>PaymobCashInOptions.TokenRefreshBuffer</c>, which defaults
    /// to 55 minutes (5 minutes before Paymob's 60-minute expiry). Changing the options at runtime
    /// invalidates the cached token on the next call.
    /// </remarks>
    /// <exception cref="OperationCanceledException">The token was canceled before a refresh request was sent.</exception>
    ValueTask<PaymobCashInResult<string>> GetAuthenticationTokenAsync(CancellationToken cancellationToken = default);
}
