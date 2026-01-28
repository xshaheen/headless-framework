// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Callback;

[PublicAPI]
public sealed class CashInCallbackTransactionSourceData
{
    [JsonPropertyName("pan")]
    public string? Pan { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("sub_type")]
    public string? SubType { get; init; }

    [JsonPropertyName("tenure")]
    public object? Tenure { get; init; }

    /// <summary>Only if Wallet or Accept Kiosk.</summary>
    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
