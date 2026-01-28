// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Models.Callback;

namespace Headless.Payments.Paymob.Services.CashIn.Models;

public enum PaymobCashInError
{
    UnknownError = 0,
    InsufficientFund = 1,
    AuthenticationFailed = 2,
    Declined = 3,
    RiskChecks = 4,
}

public static class PaymobCashInErrorExtensions
{
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
