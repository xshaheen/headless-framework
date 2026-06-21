// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Requests;

/// <summary>
/// Parameters for charging a previously saved card token without presenting a new card form.
/// </summary>
/// <param name="Amount">The charge amount in Egyptian Pounds (EGP).</param>
/// <param name="Customer">Customer contact and identity data required for the payment-key request.</param>
/// <param name="SavedTokenIntegrationId">The Paymob saved-token integration ID.</param>
/// <param name="CardToken">The saved card token issued by Paymob after the original card payment.</param>
/// <param name="MerchantOrderId">Optional merchant-side order reference.</param>
/// <param name="ExpirationSeconds">Lifetime of the payment key in seconds. Defaults to 3600 (60 minutes).</param>
public sealed record PaymobCardSavedTokenCashInRequest(
    decimal Amount,
    PaymobCashInCustomerData Customer,
    int SavedTokenIntegrationId,
    string CardToken,
    string? MerchantOrderId = null,
    int ExpirationSeconds = 60 * 60
);
