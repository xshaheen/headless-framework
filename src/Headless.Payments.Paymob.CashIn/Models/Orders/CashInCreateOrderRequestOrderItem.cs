// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Orders;

[PublicAPI]
public sealed class CashInCreateOrderRequestOrderItem
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>The line-item amount in the smallest currency unit (integer cents).</summary>
    [JsonPropertyName("amount_cents")]
    public long AmountCents { get; init; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }
}
