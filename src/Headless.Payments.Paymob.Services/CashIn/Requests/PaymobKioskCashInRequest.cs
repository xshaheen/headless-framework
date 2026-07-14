// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Requests;

/// <summary>
/// Parameters for initiating a kiosk (Aman/Accept aggregator) payment via Paymob Accept.
/// </summary>
/// <param name="Amount">The charge amount in Egyptian Pounds (EGP).</param>
/// <param name="Customer">Customer contact and identity data required for the payment-key request.</param>
/// <param name="KioskIntegrationId">The Paymob kiosk integration ID configured in the merchant dashboard.</param>
/// <param name="MerchantOrderId">Optional merchant-side order reference.</param>
/// <param name="ExpirationSeconds">Lifetime of the payment key in seconds. Defaults to 3600 (60 minutes).</param>
public sealed record PaymobKioskCashInRequest(
    decimal Amount,
    PaymobCashInCustomerData Customer,
    long KioskIntegrationId,
    string? MerchantOrderId = null,
    int ExpirationSeconds = 60 * 60
)
{
    // Customer carries PII; it is redacted so a failure log that renders this request
    // (see PaymobCashInLoggerExtensions) cannot leak it.
    public override string ToString()
    {
        return $"PaymobKioskCashInRequest {{ Amount = {Amount}, KioskIntegrationId = {KioskIntegrationId}, MerchantOrderId = {MerchantOrderId}, ExpirationSeconds = {ExpirationSeconds}, Customer = [redacted] }}";
    }
}
