// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Requests;

/// <summary>
/// Customer contact and identity data required by Paymob for payment-key and billing-data submission.
/// </summary>
/// <param name="FirstName">The customer's first name.</param>
/// <param name="LastName">The customer's last name.</param>
/// <param name="PhoneNumber">The customer's phone number.</param>
/// <param name="Email">The customer's email address.</param>
public sealed record PaymobCashInCustomerData(string FirstName, string LastName, string PhoneNumber, string Email)
{
    // All four fields are customer PII. Redact them so any record that embeds Customer — including request
    // records that have no ToString override of their own (e.g. PaymobCardCashInRequest) — cannot leak PII
    // when a failure log renders it.
    public override string ToString()
    {
        return "PaymobCashInCustomerData { FirstName = [redacted], LastName = [redacted], PhoneNumber = [redacted], Email = [redacted] }";
    }
}
