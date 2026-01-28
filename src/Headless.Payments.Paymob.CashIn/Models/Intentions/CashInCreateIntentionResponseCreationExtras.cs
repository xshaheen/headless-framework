// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Intentions;

public sealed class CashInCreateIntentionResponseCreationExtras
{
    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }

    [JsonPropertyName("merchant_order_id")]
    public required string MerchantOrderId { get; init; }
}
