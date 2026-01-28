// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Internals;
using Headless.Payments.Paymob.CashIn.Models.Orders;

namespace Headless.Payments.Paymob.CashIn.Models.Transactions;

[PublicAPI]
public sealed class CashInTransaction
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("pending")]
    public bool Pending { get; init; }

    [JsonPropertyName("error_occured")]
    public bool ErrorOccured { get; init; }

    [JsonPropertyName("amount_cents")]
    public int AmountCents { get; init; }

    /// <summary>"N/A" or decimal number</summary>
    [JsonPropertyName("fees")]
    [JsonConverter(typeof(AddNullableAmountConverter))]
    public string Fees { get; init; } = "0.0";

    /// <summary>"N/A" or decimal number</summary>
    [JsonPropertyName("vat")]
    [JsonConverter(typeof(AddNullableAmountConverter))]
    public string Vat { get; init; } = "0.0";

    [JsonPropertyName("created_at")]
    [JsonConverter(typeof(AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter))]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("paid_at")]
    [JsonConverter(typeof(AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter))]
    public DateTimeOffset? PaidAt { get; init; }

    [JsonPropertyName("is_auth")]
    public bool IsAuth { get; init; }

    [JsonPropertyName("is_capture")]
    public bool IsCapture { get; init; }

    [JsonPropertyName("refunded_amount_cents")]
    public int RefundedAmountCents { get; init; }

    [JsonPropertyName("source_id")]
    public int SourceId { get; init; }

    [JsonPropertyName("is_captured")]
    public bool IsCaptured { get; init; }

    [JsonPropertyName("captured_amount")]
    public int CapturedAmount { get; init; }

    [JsonPropertyName("owner")]
    public int Owner { get; init; }

    [JsonPropertyName("is_standalone_payment")]
    public bool IsStandalonePayment { get; init; }

    [JsonPropertyName("is_voided")]
    public bool IsVoided { get; init; }

    [JsonPropertyName("is_refunded")]
    public bool IsRefunded { get; init; }

    [JsonPropertyName("is_3d_secure")]
    public bool Is3dSecure { get; init; }

    [JsonPropertyName("is_void")]
    public bool IsVoid { get; init; }

    [JsonPropertyName("is_refund")]
    public bool IsRefund { get; init; }

    [JsonPropertyName("is_hidden")]
    public bool IsHidden { get; init; }

    [JsonPropertyName("is_live")]
    public bool IsLive { get; init; }

    [JsonPropertyName("integration_id")]
    public int IntegrationId { get; init; }

    [JsonPropertyName("profile_id")]
    public int ProfileId { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("api_source")]
    public required string ApiSource { get; init; }

    [JsonPropertyName("has_parent_transaction")]
    public bool HasParentTransaction { get; init; }

    [JsonPropertyName("terminal_id")]
    public string? TerminalId { get; init; }

    [JsonPropertyName("is_cashout")]
    public bool IsCashOut { get; init; }

    [JsonPropertyName("is_upg")]
    public bool IsUpg { get; init; }

    [JsonPropertyName("integration_type")]
    public string? IntegrationType { get; init; }

    [JsonPropertyName("card_type")]
    public string? CardType { get; init; }

    [JsonPropertyName("routing_bank")]
    public string? RoutingBank { get; init; }

    [JsonPropertyName("card_holder_bank")]
    public string? CardHolderBank { get; init; }

    [JsonPropertyName("converted_gross_amount")]
    public object? ConvertedGrossAmount { get; init; }

    [JsonPropertyName("trx_settlement_curr")]
    public object? TrxSettlementCurr { get; init; }

    [JsonPropertyName("parent_transaction")]
    public object? ParentTransaction { get; init; }

    [JsonPropertyName("wallet_transaction_type")]
    public object? WalletTransactionType { get; init; }

    [JsonPropertyName("installment")]
    public object? Installment { get; init; }

    [JsonPropertyName("merchant_staff_tag")]
    public object? MerchantStaffTag { get; init; }

    [JsonPropertyName("other_endpoint_reference")]
    public object? OtherEndpointReference { get; init; }

    [JsonPropertyName("source_data")]
    public CashInTransactionSourceData? SourceData { get; init; }

    [JsonPropertyName("data")]
    public CashInTransactionData? Data { get; init; }

    [JsonPropertyName("order")]
    public CashInOrder? Order { get; init; }

    [JsonPropertyName("billing_data")]
    public CashInTransactionBillingData? BillingData { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; }

    public bool IsCard() => string.Equals(SourceData?.Type, "card", StringComparison.Ordinal);

    public bool IsWallet() => string.Equals(SourceData?.Type, "wallet", StringComparison.Ordinal);

    public bool IsCashCollection() => string.Equals(SourceData?.Type, "cash_present", StringComparison.Ordinal);

    public bool IsAcceptKiosk() => string.Equals(SourceData?.Type, "aggregator", StringComparison.Ordinal);

    public bool IsFromIFrame() => string.Equals(ApiSource, "IFRAME", StringComparison.Ordinal);

    public bool IsInvoice() => string.Equals(ApiSource, "INVOICE", StringComparison.Ordinal);

    public bool IsInsufficientFundError() =>
        string.Equals(Data?.TxnResponseCode, "INSUFFICIENT_FUNDS", StringComparison.Ordinal);

    public bool IsAuthenticationFailedError() =>
        string.Equals(Data?.TxnResponseCode, "AUTHENTICATION_FAILED", StringComparison.Ordinal);

    public bool IsDeclinedError()
    {
        // "data.message": may be "Do not honour", or "Invalid card number", ...
        return string.Equals(Data?.TxnResponseCode, "DECLINED", StringComparison.Ordinal);
    }

    public bool IsRiskChecksError()
    {
        return Data is { TxnResponseCode: "11", Message: not null }
            && Data.Message.Contains(
                "transaction did not pass risk checks",
                StringComparison.InvariantCultureIgnoreCase
            );
    }

    public (string CardNumber, string? Type, string? Bank)? Card()
    {
        if (!IsCard())
        {
            return null;
        }

        var last4 = Data?.CardNum ?? SourceData?.Pan ?? "xxxx";
        var bank = CardHolderBank ?? "Other";
        var type = CardType ?? Data?.CardType ?? SourceData?.SubType;

        type = type?.ToUpperInvariant() switch
        {
            "MASTERCARD" => "MasterCard",
            "VISA" => "Visa",
            _ => type,
        };

        return (last4, type, string.Equals(bank, "-", StringComparison.Ordinal) ? null : bank);
    }
}
