// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;

namespace Framework.Payments.Paymob.CashIn.Models.Orders;

[PublicAPI]
public sealed class CashInOrderShippingDetails
{
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

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
    public required string WeightUnit { get; init; }

    [JsonPropertyName("contents")]
    public required string Contents { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; init; }
}
