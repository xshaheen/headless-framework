// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Transactions;

[PublicAPI]
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class CashInTransactionSourceData
{
    [JsonPropertyName("pan")]
    public string? Pan { get; init; }

    [JsonPropertyName("sub_type")]
    public required string SubType { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("tenure")]
    public object? Tenure { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
