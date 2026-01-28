// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Orders;

[PublicAPI]
public sealed class CashInCreateOrderRequestShippingDetails
{
    [JsonPropertyName("number_of_packages")]
    public int NumberOfPackages { get; init; }

    [JsonPropertyName("weight")]
    public int Weight { get; init; }

    [JsonPropertyName("length")]
    public int Length { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("weight_unit")]
    public string WeightUnit { get; init; } = "Kilogram";

    [JsonPropertyName("contents")]
    public required string Contents { get; init; }

    [JsonPropertyName("notes")]
    public required string Notes { get; init; }
}
