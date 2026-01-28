// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Internals;
using Humanizer;

namespace Headless.Payments.Paymob.CashIn.Models.Callback;

[PublicAPI]
public sealed class CashInCallbackQueryParameters
{
    [JsonPropertyName("hmac")]
    public required string Hmac { get; init; }

    [JsonPropertyName("id")]
    public required long Id { get; init; }

    [JsonPropertyName("pending")]
    public required bool Pending { get; init; }

    [JsonPropertyName("amount_cents")]
    public required int AmountCents { get; init; }

    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("is_auth")]
    public required bool IsAuth { get; init; }

    [JsonPropertyName("is_capture")]
    public required bool IsCapture { get; init; }

    [JsonPropertyName("is_standalone_payment")]
    public required bool IsStandalonePayment { get; init; }

    [JsonPropertyName("is_voided")]
    public required bool IsVoided { get; init; }

    [JsonPropertyName("is_refunded")]
    public required bool IsRefunded { get; init; }

    [JsonPropertyName("is_3d_secure")]
    public required bool Is3dSecure { get; init; }

    [JsonPropertyName("integration_id")]
    public required long IntegrationId { get; init; }

    [JsonPropertyName("has_parent_transaction")]
    public required bool HasParentTransaction { get; init; }

    [JsonPropertyName("order")]
    public required long OrderId { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("error_occured")]
    public required bool ErrorOccured { get; init; }

    [JsonPropertyName("owner")]
    public required long Owner { get; init; }

    [JsonPropertyName("source_data.type")]
    public required string SourceDataType { get; init; }

    [JsonPropertyName("source_data.pan")]
    public required string SourceDataPan { get; init; }

    [JsonPropertyName("source_data.sub_type")]
    public required string SourceDataSubType { get; init; }

    [JsonPropertyName("profile_id")]
    public long ProfileId { get; init; }

    [JsonPropertyName("merchant_commission")]
    public int MerchantCommission { get; init; }

    [JsonPropertyName("accept_fees")]
    public int AcceptFees { get; init; }

    [JsonPropertyName("is_void")]
    public bool IsVoid { get; init; }

    [JsonPropertyName("is_refund")]
    public bool IsRefund { get; init; }

    [JsonPropertyName("refunded_amount_cents")]
    public int RefundedAmountCents { get; init; }

    [JsonPropertyName("captured_amount")]
    public int CapturedAmount { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; init; }

    [JsonPropertyName("is_settled")]
    public bool IsSettled { get; init; }

    [JsonPropertyName("bill_balanced")]
    public bool BillBalanced { get; init; }

    [JsonPropertyName("is_bill")]
    public bool IsBill { get; init; }

    [JsonPropertyName("merchant_order_id")]
    public string? MerchantOrderId { get; init; }

    [JsonPropertyName("data.message")]
    public string? DataMessage { get; init; }

    [JsonPropertyName("acq_response_code")]
    public string? AcqResponseCode { get; init; }

    [JsonPropertyName("discount_details")]
    public object[] DiscountDetails { get; init; } = [];

    [JsonPropertyName("txn_response_code")]
    public string? TxnResponseCode { get; init; }

    /// <summary>Return the concatenated string of transaction.</summary>
    public string ToConcatenatedString()
    {
        static string toString(bool value)
        {
            return value ? "true" : "false";
        }

        return AmountCents.ToString(CultureInfo.InvariantCulture)
            + CreatedAt
            + Currency
            + toString(ErrorOccured)
            + toString(HasParentTransaction)
            + Id.ToString(CultureInfo.InvariantCulture)
            + IntegrationId.ToString(CultureInfo.InvariantCulture)
            + toString(Is3dSecure)
            + toString(IsAuth)
            + toString(IsCapture)
            + toString(IsRefunded)
            + toString(IsStandalonePayment)
            + toString(IsVoided)
            + OrderId.ToString(CultureInfo.InvariantCulture)
            + Owner.ToString(CultureInfo.InvariantCulture)
            + toString(Pending)
            + SourceDataPan?.ToLowerInvariant()
            + SourceDataSubType
            + SourceDataType?.ToLowerInvariant()
            + toString(Success);
    }

    public DateTimeOffset CreatedAtDateTimeOffset()
    {
        var dateTime = DateTime.Parse(CreatedAt, CultureInfo.InvariantCulture);

        // If not have time zone offset, consider it cairo time.
        return dateTime.Kind is DateTimeKind.Unspecified
            ? new DateTimeOffset(
                dateTime,
                AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter.EgyptTimeZone.GetUtcOffset(dateTime)
            )
            : DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture);
    }

    public bool IsCard()
    {
        return string.Equals(SourceDataType, "card", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsWallet()
    {
        return string.Equals(SourceDataType, "wallet", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsCashCollection()
    {
        return string.Equals(SourceDataType, "cash_present", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsAcceptKiosk()
    {
        return string.Equals(SourceDataType, "aggregator", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsInsufficientFundError()
    {
        return string.Equals(TxnResponseCode, "INSUFFICIENT_FUNDS", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsAuthenticationFailedError()
    {
        return string.Equals(TxnResponseCode, "AUTHENTICATION_FAILED", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsDeclinedError()
    {
        // "data.message": may be "Do not honour", or "Invalid card number", ...
        return string.Equals(TxnResponseCode, "DECLINED", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsRiskChecksError()
    {
        return TxnResponseCode is "11"
            && DataMessage?.Contains(
                "transaction did not pass risk checks",
                StringComparison.InvariantCultureIgnoreCase
            ) == true;
    }

    public CashInCardInfo? Card()
    {
        if (!IsCard())
        {
            return null;
        }

        var type = SourceDataSubType?.ToUpperInvariant() switch
        {
            "MASTERCARD" => "MasterCard",
            "VISA" => "Visa",
            null => null,
            _ => SourceDataSubType.Humanize(),
        };

        return new(SourceDataPan, type, Bank: null);
    }
}
