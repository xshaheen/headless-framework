// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashOut.Models;

/// <summary>
/// Aman kiosk-specific details returned inside a CashOut disbursement response when the
/// issuer is <c>aman</c> (Accept channel).
/// </summary>
[PublicAPI]
public sealed record CashOutDisburseResponseAmanCashingDetails
{
    /// <summary>
    /// The billing reference number the recipient presents at an Aman outlet to collect the cash.
    /// <see langword="null"/> when not yet assigned.
    /// </summary>
    [JsonPropertyName("bill_reference")]
    public long? BillingReference { get; init; }

    /// <summary>
    /// Indicates whether the recipient has already collected the cash at the kiosk.
    /// </summary>
    [JsonPropertyName("is_paid")]
    public bool IsPaid { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
