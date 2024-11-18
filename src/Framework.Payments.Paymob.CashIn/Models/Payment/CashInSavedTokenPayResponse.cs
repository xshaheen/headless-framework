// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;
using Framework.Payments.Paymob.CashIn.Internal;

namespace Framework.Payments.Paymob.CashIn.Models.Payment;

[PublicAPI]
public sealed class CashInSavedTokenPayResponse
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("redirection_url")]
    public required string RedirectionUrl { get; init; }

    [JsonPropertyName("success")]
    public required string Success { get; init; }

    [JsonPropertyName("pending")]
    public required string Pending { get; init; }

    [JsonPropertyName("amount_cents")]
    public int AmountCents { get; init; }

    [JsonPropertyName("is_auth")]
    public required string IsAuth { get; init; }

    [JsonPropertyName("is_capture")]
    public required string IsCapture { get; init; }

    [JsonPropertyName("is_standalone_payment")]
    public required string IsStandalonePayment { get; init; }

    [JsonPropertyName("is_voided")]
    public required string IsVoided { get; init; }

    [JsonPropertyName("is_refunded")]
    public required string IsRefunded { get; init; }

    [JsonPropertyName("is_3d_secure")]
    public required string Is3dSecure { get; init; }

    [JsonPropertyName("integration_id")]
    public int IntegrationId { get; init; }

    [JsonPropertyName("profile_id")]
    public int ProfileId { get; init; }

    [JsonPropertyName("has_parent_transaction")]
    public required string HasParentTransaction { get; init; }

    [JsonPropertyName("order")]
    public int Order { get; init; }

    [JsonPropertyName("created_at")]
    [JsonConverter(typeof(AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter))]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("terminal_id")]
    public string? TerminalId { get; init; }

    [JsonPropertyName("merchant_commission")]
    public int MerchantCommission { get; init; }

    [JsonPropertyName("is_void")]
    public required string IsVoid { get; init; }

    [JsonPropertyName("is_refund")]
    public required string IsRefund { get; init; }

    [JsonPropertyName("error_occured")]
    public required string ErrorOccured { get; init; }

    [JsonPropertyName("refunded_amount_cents")]
    public int RefundedAmountCents { get; init; }

    [JsonPropertyName("captured_amount")]
    public int CapturedAmount { get; init; }

    [JsonPropertyName("merchant_staff_tag")]
    public string? MerchantStaffTag { get; init; }

    [JsonPropertyName("owner")]
    public int Owner { get; init; }

    [JsonPropertyName("parent_transaction")]
    public string? ParentTransaction { get; init; }

    [JsonPropertyName("merchant_order_id")]
    public string? MerchantOrderId { get; init; }

    [JsonPropertyName("data.message")]
    public string? DataMessage { get; init; }

    [JsonPropertyName("source_data.type")]
    public string? SourceDataType { get; init; }

    [JsonPropertyName("source_data.pan")]
    public string? SourceDataPan { get; init; }

    [JsonPropertyName("source_data.sub_type")]
    public string? SourceDataSubType { get; init; }

    [JsonPropertyName("acq_response_code")]
    public string? AcqResponseCode { get; init; }

    [JsonPropertyName("txn_response_code")]
    public string? TxnResponseCode { get; init; }

    [JsonPropertyName("hmac")]
    public required string Hmac { get; init; }

    [JsonPropertyName("use_redirection")]
    public bool UseRedirection { get; init; }

    [JsonPropertyName("merchant_response")]
    public string? MerchantResponse { get; init; }

    [JsonPropertyName("bypass_step_six")]
    public bool BypassStepSix { get; init; }

    public bool IsCreatedSuccessfully()
    {
        return string.Equals(Success, "false", StringComparison.Ordinal) && string.Equals(Pending, "true", StringComparison.Ordinal) && string.Equals(ErrorOccured, "false", StringComparison.Ordinal);
    }
}
