// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using System.Text.Json.Serialization;

namespace Framework.Payments.Paymob.CashIn.Models.Callback;

[PublicAPI]
public sealed class CashInCallbackTransactionDataAcquirer
{
    [JsonPropertyName("settlementDate")]
    public required string SettlementDate { get; init; }

    [JsonPropertyName("timeZone")]
    public required string TimeZone { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("date")]
    public required string Date { get; init; }

    [JsonPropertyName("merchantId")]
    public required string MerchantId { get; init; }

    [JsonPropertyName("transactionId")]
    public required string TransactionId { get; init; }

    [JsonPropertyName("batch")]
    public int Batch { get; init; }
}
