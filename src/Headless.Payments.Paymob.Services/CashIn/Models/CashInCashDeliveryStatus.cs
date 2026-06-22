// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Models;

/// <summary>
/// The webhook envelope for a Paymob cash-collection delivery status update.
/// </summary>
public sealed class CashInCashDeliveryStatus
{
    /// <summary>The event type discriminator sent by Paymob.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The delivery status payload, present when <c>Type</c> indicates a delivery event.</summary>
    [JsonPropertyName("obj")]
    public CashInCashDeliveryStatusObj? Obj { get; init; }
}

/// <summary>
/// The delivery event payload inside a <c>CashInCashDeliveryStatus</c> callback, describing the
/// current courier status of a cash-collection order.
/// </summary>
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
