// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using Framework.Payments.Paymob.CashIn.Models.Auth;

namespace Framework.Payments.Paymob.CashIn;

public interface IPaymobCashInAuthenticator
{
    /// <summary>Request a new authentication token.</summary>
    /// <returns>Authentication token response.</returns>
    /// <exception cref="PaymobRequestException"></exception>
    [Pure]
    Task<CashInAuthenticationTokenResponse> RequestAuthenticationTokenAsync();

    /// <summary>Get an authentication token from cache if is valid or request a new one.</summary>
    /// <returns>Authentication token, which is valid for 1 hour from the creation time.</returns>
    /// <exception cref="PaymobRequestException"></exception>
    [Pure]
    ValueTask<string> GetAuthenticationTokenAsync();
}
