// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Payment;

[PublicAPI]
public sealed class CashInKioskPayData
{
    [JsonPropertyName("gateway_integration_pk")]
    public int GatewayIntegrationPk { get; init; }

    [JsonPropertyName("otp")]
    public required string Otp { get; init; }

    [JsonPropertyName("klass")]
    public required string Klass { get; init; }

    [JsonPropertyName("txn_response_code")]
    public required string TxnResponseCode { get; init; }

    [JsonPropertyName("bill_reference")]
    public int BillReference { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("paid_through")]
    public required string PaidThrough { get; init; }

    [JsonPropertyName("due_amount")]
    public int DueAmount { get; init; }

    [JsonPropertyName("biller")]
    public object? Biller { get; init; }

    [JsonPropertyName("from_user")]
    public object? FromUser { get; init; }

    [JsonPropertyName("ref")]
    public object? Ref { get; init; }

    [JsonPropertyName("cashout_amount")]
    public object? CashOutAmount { get; init; }

    [JsonPropertyName("agg_terminal")]
    public object? AggTerminal { get; init; }

    [JsonPropertyName("amount")]
    public object? Amount { get; init; }

    [JsonPropertyName("rrn")]
    public object? Rrn { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
