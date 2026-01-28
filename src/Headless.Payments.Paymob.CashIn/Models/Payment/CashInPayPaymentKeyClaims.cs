// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Payment;

[PublicAPI]
public sealed class CashInPayPaymentKeyClaims
{
    [JsonPropertyName("integration_id")]
    public int IntegrationId { get; init; }

    [JsonPropertyName("amount_cents")]
    public int AmountCents { get; init; }

    [JsonPropertyName("user_id")]
    public int UserId { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("exp")]
    public int Exp { get; init; }

    [JsonPropertyName("order_id")]
    public int OrderId { get; init; }

    [JsonPropertyName("pmk_ip")]
    public string? PmkIp { get; init; }

    [JsonPropertyName("lock_order_when_paid")]
    public bool LockOrderWhenPaid { get; init; }

    [JsonPropertyName("billing_data")]
    public CashInPayPaymentKeyClaimsBillingData? BillingData { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
