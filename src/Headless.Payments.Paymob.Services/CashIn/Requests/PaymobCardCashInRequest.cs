// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Requests;

/// <summary>
/// Parameters for initiating a card payment via the Paymob Accept hosted iframe.
/// </summary>
/// <param name="Amount">The charge amount in Egyptian Pounds (EGP).</param>
/// <param name="Customer">Customer contact and identity data included in the payment-key request.</param>
/// <param name="CardIntegrationId">The Paymob card integration ID configured in the merchant dashboard.</param>
/// <param name="IframeSrc">
/// The Paymob iframe integration ID appended to the base iframe URL. This value is account-specific;
/// obtain it from the iframe configured in your Paymob merchant dashboard.
/// </param>
/// <param name="MerchantOrderId">Optional merchant-side order reference correlated with the Paymob order.</param>
/// <param name="ExpirationSeconds">
/// Lifetime of the payment key in seconds. Defaults to 3600 (60 minutes).
/// The customer must complete payment before this period elapses.
/// </param>
public sealed record PaymobCardCashInRequest(
    decimal Amount,
    PaymobCashInCustomerData Customer,
    long CardIntegrationId,
    string IframeSrc,
    string? MerchantOrderId = null,
    int ExpirationSeconds = 60 * 60
);
