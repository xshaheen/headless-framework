// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Internals;
using Headless.Payments.Paymob.CashIn.Models.Orders;

namespace Headless.Payments.Paymob.CashIn.Models.Payment;

[PublicAPI]
public sealed class CashInWalletPayResponse
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("pending")]
    public bool Pending { get; init; }

    [JsonPropertyName("amount_cents")]
    public long AmountCents { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("is_auth")]
    public bool IsAuth { get; init; }

    [JsonPropertyName("is_capture")]
    public bool IsCapture { get; init; }

    [JsonPropertyName("is_standalone_payment")]
    public bool IsStandalonePayment { get; init; }

    [JsonPropertyName("is_voided")]
    public bool IsVoided { get; init; }

    [JsonPropertyName("is_refunded")]
    public bool IsRefunded { get; init; }

    [JsonPropertyName("is_3d_secure")]
    public bool Is3DSecure { get; init; }

    [JsonPropertyName("integration_id")]
    public long IntegrationId { get; init; }

    [JsonPropertyName("profile_id")]
    public long ProfileId { get; init; }

    [JsonPropertyName("has_parent_transaction")]
    public bool HasParentTransaction { get; init; }

    [JsonPropertyName("created_at")]
    [JsonConverter(typeof(AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter))]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("api_source")]
    public required string ApiSource { get; init; }

    [JsonPropertyName("merchant_commission")]
    public long MerchantCommission { get; init; }

    [JsonPropertyName("is_void")]
    public bool IsVoid { get; init; }

    [JsonPropertyName("is_refund")]
    public bool IsRefund { get; init; }

    [JsonPropertyName("is_hidden")]
    public bool IsHidden { get; init; }

    [JsonPropertyName("is_live")]
    public bool IsLive { get; init; }

    [JsonPropertyName("refunded_amount_cents")]
    public long RefundedAmountCents { get; init; }

    [JsonPropertyName("source_id")]
    public long SourceId { get; init; }

    [JsonPropertyName("is_captured")]
    public bool IsCaptured { get; init; }

    [JsonPropertyName("captured_amount")]
    public long CapturedAmount { get; init; }

    [JsonPropertyName("owner")]
    public long Owner { get; init; }

    [JsonPropertyName("error_occured")]
    public bool ErrorOccured { get; init; }

    [JsonPropertyName("other_endpoint_reference")]
    public required string OtherEndpointReference { get; init; }

    [JsonPropertyName("terminal_id")]
    public string? TerminalId { get; init; }

    [JsonPropertyName("redirect_url")]
    public string? RedirectUrl { get; init; }

    [JsonPropertyName("iframe_redirection_url")]
    public string? IframeRedirectionUrl { get; init; }

    [JsonPropertyName("data")]
    public required CashInWalletData Data { get; init; }

    [JsonPropertyName("source_data")]
    public required CashInWalletPaySourceData PaySourceData { get; init; }

    [JsonPropertyName("order")]
    public required CashInOrder Order { get; init; }

    [JsonPropertyName("payment_key_claims")]
    public required CashInPayPaymentKeyClaims PaymentKeyClaims { get; init; }

    [JsonPropertyName("merchant_staff_tag")]
    public object? MerchantStaffTag { get; init; }

    [JsonPropertyName("parent_transaction")]
    public object? ParentTransaction { get; init; }

    [JsonPropertyName("transaction_processed_callback_responses")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<object?> TransactionProcessedCallbackResponses
    {
        get => field ?? [];
        init;
    }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }

    public bool IsCreatedSuccessfully()
    {
        return !Success && Pending;
    }
}
