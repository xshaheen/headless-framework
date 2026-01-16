// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Payment;

[PublicAPI]
public sealed class CashInKioskPaySourceData
{
    [JsonPropertyName("sub_type")]
    public required string SubType { get; init; }

    [JsonPropertyName("pan")]
    public required string Pan { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
