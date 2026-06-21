// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Models.Callback;

namespace Headless.Payments.Paymob.Services.CashIn.Models;

/// <summary>
/// Categorises the failure reason of a Paymob card transaction received via callback.
/// </summary>
public enum PaymobCashInError
{
    /// <summary>The failure reason is not one of the recognised categories.</summary>
    UnknownError = 0,

    /// <summary>The card was declined because the customer has insufficient funds.</summary>
    InsufficientFund = 1,

    /// <summary>The card issuer rejected the transaction due to an authentication failure (e.g., wrong OTP).</summary>
    AuthenticationFailed = 2,

    /// <summary>The issuing bank declined the transaction (generic decline).</summary>
    Declined = 3,

    /// <summary>Paymob's fraud management system rejected the transaction during risk checks.</summary>
    RiskChecks = 4,
}

/// <summary>
/// Extension methods for interpreting Paymob transaction callback error states.
/// </summary>
public static class PaymobCashInErrorExtensions
{
    /// <summary>
    /// Maps the failure data on a callback transaction to a <c>PaymobCashInError</c> category.
    /// </summary>
    /// <param name="transaction">The failed transaction from the Paymob callback.</param>
    /// <returns>The matching <c>PaymobCashInError</c> value, or <c>UnknownError</c> when the reason is unrecognised.</returns>
    public static PaymobCashInError GetError(this CashInCallbackTransaction transaction)
    {
        if (transaction.IsInsufficientFundError())
        {
            return PaymobCashInError.InsufficientFund;
        }

        if (transaction.IsAuthenticationFailedError())
        {
            return PaymobCashInError.AuthenticationFailed;
        }

        if (transaction.IsDeclinedError())
        {
            return PaymobCashInError.Declined;
        }

        return transaction.IsRiskChecksError() ? PaymobCashInError.RiskChecks : PaymobCashInError.UnknownError;
    }
}
