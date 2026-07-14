// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Intentions;

public sealed class CashInCreateIntentionResponseIntentionDetail
{
    /// <summary>The intention amount in the smallest currency unit (integer cents).</summary>
    [JsonPropertyName("amount")]
    public long Amount { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("items")]
    public required List<CashInCreateIntentionResponseItem> Items { get; init; }

    [JsonPropertyName("billing_data")]
    public required CashInCreateIntentionResponseBillingData BillingData { get; init; }
}
