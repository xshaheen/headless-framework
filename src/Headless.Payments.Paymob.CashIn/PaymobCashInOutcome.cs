// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn;

/// <summary>What Paymob did with a CashIn request, as far as the caller can know.</summary>
/// <remarks>
/// There is deliberately no "failed" value: a failure is either <see cref="Rejected"/>, which is safe to
/// correct and send again, or <see cref="Unknown"/>, which may already have moved money.
/// <see cref="Unknown"/> is the zero value, so an outcome that was never set reads as unknown, never as succeeded.
/// </remarks>
[PublicAPI]
public enum PaymobCashInOutcome
{
    /// <summary>
    /// The request may have been applied: Paymob answered with a 5xx status or an unreadable body, the call
    /// timed out, or the connection failed after the request may have been sent. Resolve it by callback or
    /// inquiry before you send the request again.
    /// </summary>
    Unknown = 0,

    /// <summary>Paymob accepted the request. A payment, refund, or void may still be pending.</summary>
    Succeeded = 1,

    /// <summary>
    /// Paymob definitely did not apply the request: it answered with a 4xx status, reported a business
    /// rejection such as a declined charge, or the request never left because authentication failed.
    /// </summary>
    Rejected = 2,
}
