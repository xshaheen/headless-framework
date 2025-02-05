// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Intentions;

public sealed class CashInCreateIntentionResponseIntentionDetail
{
    [JsonPropertyName("amount")]
    public int Amount { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("items")]
    public required List<CashInCreateIntentionResponseItem> Items { get; init; }

    [JsonPropertyName("billing_data")]
    public required CashInCreateIntentionResponseBillingData BillingData { get; init; }
}
