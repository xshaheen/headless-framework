// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Callback;

[PublicAPI]
public sealed class CashInCallbackTransactionDataMigsTransaction
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("frequency")]
    public string? Frequency { get; init; }

    [JsonPropertyName("authorizationCode")]
    public string? AuthorizationCode { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("receipt")]
    public string? Receipt { get; init; }

    [JsonPropertyName("terminal")]
    public string? Terminal { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("acquirer")]
    public CashInCallbackTransactionDataAcquirer? Acquirer { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; init; }
}
