// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Intentions;

public sealed class CashInCreateIntentionRequestItem
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>The line-item amount in the smallest currency unit (integer cents).</summary>
    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}
