// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Payment;

internal sealed class CashInPaymentKeyInternalRequest(
    CashInPaymentKeyRequest request,
    string authToken,
    int defaultExpiration
)
{
    [JsonPropertyName("auth_token")]
    public string AuthToken { get; } = authToken;

    [JsonPropertyName("integration_id")]
    public int IntegrationId { get; } = request.IntegrationId;

    [JsonPropertyName("order_id")]
    public int OrderId { get; } = request.OrderId;

    [JsonPropertyName("amount_cents")]
    public string AmountCents { get; } = request.AmountCents.ToString(CultureInfo.InvariantCulture);

    [JsonPropertyName("expiration")]
    public int Expiration { get; } = request.Expiration ?? defaultExpiration;

    [JsonPropertyName("currency")]
    public string Currency { get; } = request.Currency;

    [JsonPropertyName("lock_order_when_paid")]
    public string LockOrderWhenPaid { get; } = request.LockOrderWhenPaid ? "true" : "false";

    [JsonPropertyName("billing_data")]
    public CashInBillingData BillingData { get; } = request.BillingData;
}
