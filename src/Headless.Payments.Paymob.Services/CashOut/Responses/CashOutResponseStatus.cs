// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashOut.Responses;

/// <summary>
/// Indicates the outcome state of a CashOut disbursement.
/// </summary>
public enum CashOutResponseStatus
{
    /// <summary>
    /// The provider accepted the disbursement request but has not yet confirmed delivery.
    /// Monitor via the inquiry endpoint or await a callback.
    /// </summary>
    Pending,

    /// <summary>The disbursement was delivered to the recipient successfully.</summary>
    Success,
}
