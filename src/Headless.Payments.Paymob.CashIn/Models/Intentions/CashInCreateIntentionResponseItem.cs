// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Intentions;

public sealed class CashInCreateIntentionResponseItem
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The line-item amount in the smallest currency unit (integer cents).</summary>
    [JsonPropertyName("amount")]
    public required long Amount { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("quantity")]
    public required int Quantity { get; init; }

    [JsonPropertyName("image")]
    public object? Image { get; init; }
}
