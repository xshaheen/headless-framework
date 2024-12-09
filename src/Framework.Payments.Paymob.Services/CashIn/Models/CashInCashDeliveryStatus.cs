// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.Services.CashIn.Models;

public sealed class CashInCashDeliveryStatus
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("obj")]
    public CashInCashDeliveryStatusObj? Obj { get; init; }
}

public sealed class CashInCashDeliveryStatusObj
{
    [JsonPropertyName("order_id")]
    public int OrderId { get; init; }

    [JsonPropertyName("order_delivery_status")]
    public required string OrderDeliveryStatus { get; init; }

    [JsonPropertyName("merchant_id")]
    public int MerchantId { get; init; }

    [JsonPropertyName("merchant_name")]
    public string? MerchantName { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; init; }
}
