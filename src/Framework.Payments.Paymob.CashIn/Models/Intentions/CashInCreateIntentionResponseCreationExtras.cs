// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Intentions;

public sealed class CashInCreateIntentionResponseCreationExtras
{
    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; init; }

    [JsonPropertyName("merchant_order_id")]
    public required string MerchantOrderId { get; init; }
}
