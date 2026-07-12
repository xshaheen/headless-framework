// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Models;
using Headless.Payments.Paymob.CashIn.Models.Auth;

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
    /// <returns>The full authentication response including the bearer token and merchant profile.</returns>
    /// <exception cref="PaymobCashInException">The HTTP request to Paymob failed.</exception>
    Task<CashInAuthenticationTokenResponse> RequestAuthenticationTokenAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns a valid authentication token, using a cached value when it has not yet expired.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the network call if a refresh is needed.</param>
    /// <returns>
    /// A bearer token string that is valid for use in Paymob API requests. The cached token is
    /// returned when it remains valid; otherwise a fresh token is fetched and cached.
    /// </returns>
    /// <remarks>
    /// Token lifetime is controlled by <c>PaymobCashInOptions.TokenRefreshBuffer</c>, which defaults
    /// to 55 minutes (5 minutes before Paymob's 60-minute expiry). Changing the options at runtime
    /// invalidates the cached token on the next call.
    /// </remarks>
    /// <exception cref="PaymobCashInException">The HTTP request to Paymob failed during a token refresh.</exception>
    ValueTask<string> GetAuthenticationTokenAsync(CancellationToken cancellationToken = default);
}
