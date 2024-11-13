// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;
using Framework.Payments.Paymob.CashIn.Internal;

namespace Framework.Payments.Paymob.CashIn.Models.Payment;

[PublicAPI]
public sealed class CashInWalletData
{
    [JsonPropertyName("created_at")]
    [JsonConverter(typeof(AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter))]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("gateway_integration_pk")]
    public int GatewayIntegrationPk { get; init; }

    [JsonPropertyName("redirect_url")]
    public required string RedirectUrl { get; init; }

    [JsonPropertyName("klass")]
    public required string Klass { get; init; }

    [JsonPropertyName("mpg_txn_id")]
    public required string MpgTxnId { get; init; }

    [JsonPropertyName("txn_response_code")]
    public string? TxnResponseCode { get; init; }

    [JsonPropertyName("uig_txn_id")]
    public string? UigTxnId { get; init; }

    [JsonPropertyName("upg_txn_id")]
    public string? UpgTxnId { get; init; }

    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("order_info")]
    public required string OrderInfo { get; init; }

    [JsonPropertyName("method")]
    public int Method { get; init; }

    [JsonPropertyName("wallet_issuer")]
    public required string WalletIssuer { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("amount")]
    public int Amount { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("mer_txn_ref")]
    public required string MerTxnRef { get; init; }

    [JsonPropertyName("upg_qrcode_ref")]
    public required string UpgQrcodeRef { get; init; }

    [JsonPropertyName("wallet_msisdn")]
    public required string WalletMsisdn { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; init; }
}
