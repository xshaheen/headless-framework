// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Requests;

/// <summary>
/// Parameters for initiating a mobile-wallet payment via Paymob Accept.
/// </summary>
/// <param name="Amount">The charge amount in Egyptian Pounds (EGP).</param>
/// <param name="WalletPhoneNumber">
/// The customer's wallet-registered phone number. The customer receives an OTP at this number.
/// </param>
/// <param name="Customer">Customer contact and identity data required for the payment-key request.</param>
/// <param name="WalletIntegrationId">The Paymob wallet integration ID configured in the merchant dashboard.</param>
/// <param name="MerchantOrderId">Optional merchant-side order reference.</param>
/// <param name="ExpirationSeconds">Lifetime of the payment key in seconds. Defaults to 3600 (60 minutes).</param>
public sealed record PaymobWalletCashInRequest(
    decimal Amount,
    string WalletPhoneNumber,
    PaymobCashInCustomerData Customer,
    long WalletIntegrationId,
    string? MerchantOrderId = null,
    int ExpirationSeconds = 60 * 60
);
