// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Payment;

[PublicAPI]
public sealed class CashInWalletPaySourceData
{
    [JsonPropertyName("owner_name")]
    public string? OwnerName { get; init; }

    [JsonPropertyName("sub_type")]
    public required string SubType { get; init; }

    [JsonPropertyName("pan")]
    public required string Pan { get; init; }

    [JsonPropertyName("phone_number")]
    public required string PhoneNumber { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; init; }
}
