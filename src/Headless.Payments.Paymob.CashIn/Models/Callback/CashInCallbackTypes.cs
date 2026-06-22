// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Callback;

/// <summary>
/// Well-known values for the <c>type</c> field in a Paymob Accept callback body.
/// </summary>
public static class CashInCallbackTypes
{
    /// <summary>
    /// The callback represents a payment transaction event (charge, refund, void, etc.).
    /// The <c>obj</c> field should be deserialized as <c>CashInCallbackTransaction</c>.
    /// </summary>
    public const string Transaction = "TRANSACTION";

    /// <summary>
    /// The callback represents a saved-card tokenisation event.
    /// The <c>obj</c> field should be deserialized as <c>CashInCallbackToken</c>.
    /// </summary>
    public const string Token = "TOKEN";
}
