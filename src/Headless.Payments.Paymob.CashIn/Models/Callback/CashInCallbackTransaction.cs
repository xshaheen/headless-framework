// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Internals;
using Headless.Payments.Paymob.CashIn.Models.Payment;
using Humanizer;

namespace Headless.Payments.Paymob.CashIn.Models.Callback;

/// <summary>
/// Represents the full transaction object delivered by Paymob in a callback webhook or returned
/// from the broker's refund and void operations.
/// </summary>
/// <remarks>
/// When received as a webhook, verify authenticity with
/// <c>IPaymobCashInBroker.Validate(CashInCallbackTransaction, string)</c> before acting on it.
/// Use the helper methods (<c>IsCard</c>, <c>IsWallet</c>, <c>IsSuccess</c>, etc.) to interrogate
/// the transaction's channel and outcome without string-matching raw field values directly.
/// </remarks>
[PublicAPI]
public sealed class CashInCallbackTransaction
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    /// <summary>
    /// It's indicating the amount paid to this transaction, it might be different from
    /// the original order price, and it is in cents.
    /// </summary>
    [JsonPropertyName("amount_cents")]
    public long AmountCents { get; init; }

    /// <summary>
    /// True in one of these cases:
    /// <para>Card Payments: The customer has been redirected to the issuing bank page to enter his OTP.</para>
    /// <para>Kiosk Payments: A payment reference number was generated, and it is pending to be paid.</para>
    /// <para>
    /// Cash Payments: Your cash payment is ready to be collected, and the courier is on his way to collect it from your
    /// customer.
    /// </para>
    /// </summary>
    [JsonPropertyName("pending")]
    public bool Pending { get; init; }

    /// <summary>
    /// A boolean-valued key indicating the status of the transaction whether it was successful
    /// or not, it would be true if your customer has successfully performed his payment.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// A boolean-valued key indicating if this was an authorized transaction, learn more about
    /// auth/cap transactions.
    /// </summary>
    [JsonPropertyName("is_auth")]
    public bool IsAuth { get; init; }

    /// <summary>
    /// A boolean-valued key indicating if this was a capture transaction, learn more about
    /// auth/cap transactions.
    /// </summary>
    [JsonPropertyName("is_capture")]
    public bool IsCapture { get; init; }

    /// <summary>
    /// A boolean-valued key indicating if this transaction was voided or not, learn more about
    /// void and refund transaction.
    /// </summary>
    [JsonPropertyName("is_voided")]
    public bool IsVoided { get; init; }

    /// <summary>
    /// A boolean-valued key indicating if this transaction was refunded or not, learn more
    /// about void and refund transaction.
    /// </summary>
    [JsonPropertyName("is_refunded")]
    public bool IsRefunded { get; init; }

    /// <summary>
    /// A boolean-valued key indicating if this transaction was 3D secured or not, learn more
    /// about the 3D and moto transactions.
    /// </summary>
    [JsonPropertyName("is_3d_secure")]
    public bool Is3DSecure { get; init; }

    [JsonPropertyName("is_standalone_payment")]
    public bool IsStandalonePayment { get; init; }

    [JsonPropertyName("integration_id")]
    public long IntegrationId { get; init; }

    [JsonPropertyName("profile_id")]
    public long ProfileId { get; init; }

    [JsonPropertyName("has_parent_transaction")]
    public bool HasParentTransaction { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("api_source")]
    public string? ApiSource { get; init; }

    [JsonPropertyName("merchant_commission")]
    public long MerchantCommission { get; init; }

    [JsonPropertyName("is_void")]
    public bool IsVoid { get; init; }

    [JsonPropertyName("is_refund")]
    public bool IsRefund { get; init; }

    [JsonPropertyName("is_hidden")]
    public bool IsHidden { get; init; }

    [JsonPropertyName("error_occured")]
    public bool ErrorOccurred { get; init; }

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

    [JsonPropertyName("terminal_id")]
    public string? TerminalId { get; init; }

    [JsonPropertyName("data")]
    public CashInCallbackTransactionData? Data { get; init; }

    [JsonPropertyName("order")]
    public required CashInCallbackTransactionOrder Order { get; init; }

    [JsonPropertyName("payment_key_claims")]
    public CashInPayPaymentKeyClaims? PaymentKeyClaims { get; init; }

    [JsonPropertyName("source_data")]
    public CashInCallbackTransactionSourceData? SourceData { get; init; }

    [JsonPropertyName("transaction_processed_callback_responses")]
    public IReadOnlyList<TransactionProcessedCallbackResponse> TransactionProcessedCallbackResponses
    {
        get => field ?? [];
        init;
    } = null!;

    /// <summary>Opaque Paymob passthrough value; shape is provider-defined and usually <see langword="null"/>.</summary>
    [JsonPropertyName("other_endpoint_reference")]
    public object? OtherEndpointReference { get; init; }

    /// <summary>Opaque Paymob passthrough value; shape is provider-defined and usually <see langword="null"/>.</summary>
    [JsonPropertyName("merchant_staff_tag")]
    public object? MerchantStaffTag { get; init; }

    /// <summary>Identifier of the parent transaction when this transaction is a refund or void; otherwise <see langword="null"/>.</summary>
    [JsonPropertyName("parent_transaction")]
    public long? ParentTransaction { get; init; }

    /// <summary>Unmodelled JSON fields returned by Paymob, captured so no callback data is lost.</summary>
    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }

    /// <summary>
    /// Produces the HMAC input string for this transaction by concatenating the required fields
    /// in Paymob's defined order.
    /// </summary>
    /// <remarks>Pass the result directly to <c>IPaymobCashInBroker.Validate(string, string)</c>.</remarks>
    public string ToConcatenatedString()
    {
        static string toString(bool value)
        {
            return value ? "true" : "false";
        }

        return AmountCents.ToString(CultureInfo.InvariantCulture)
            + CreatedAt
            + Currency
            + toString(ErrorOccurred)
            + toString(HasParentTransaction)
            + Id.ToString(CultureInfo.InvariantCulture)
            + IntegrationId.ToString(CultureInfo.InvariantCulture)
            + toString(Is3DSecure)
            + toString(IsAuth)
            + toString(IsCapture)
            + toString(IsRefunded)
            + toString(IsStandalonePayment)
            + toString(IsVoided)
            + Order.Id.ToString(CultureInfo.InvariantCulture)
            + Owner.ToString(CultureInfo.InvariantCulture)
            + toString(Pending)
            + SourceData?.Pan?.ToLowerInvariant()
            + SourceData?.SubType
            + SourceData?.Type?.ToLowerInvariant()
            + toString(Success);
    }

    /// <summary>
    /// Parses <c>CreatedAt</c> into a <see cref="DateTimeOffset"/>, applying the Egypt/Cairo UTC+2
    /// offset when the raw string carries no timezone information.
    /// </summary>
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

    /// <summary>Returns <see langword="true"/> when the payment method was a credit or debit card.</summary>
    public bool IsCard()
    {
        return string.Equals(SourceData?.Type, "card", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns <see langword="true"/> when the payment method was a mobile wallet (e.g., Vodafone Cash).</summary>
    public bool IsWallet()
    {
        return string.Equals(SourceData?.Type, "wallet", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns <see langword="true"/> when the payment method was cash collection by courier.</summary>
    public bool IsCashCollection()
    {
        return string.Equals(SourceData?.Type, "cash_present", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns <see langword="true"/> when the payment was made at an Aman kiosk (aggregator channel).</summary>
    public bool IsAcceptKiosk()
    {
        return string.Equals(SourceData?.Type, "aggregator", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns <see langword="true"/> when the transaction was initiated through the hosted card-payment iframe.</summary>
    public bool IsFromIFrame()
    {
        return string.Equals(ApiSource, "IFRAME", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns <see langword="true"/> when the transaction was initiated via a Paymob-generated invoice link.</summary>
    public bool IsInvoice()
    {
        return string.Equals(ApiSource, "INVOICE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the failure reason is insufficient funds on the customer's card.
    /// </summary>
    public bool IsInsufficientFundError()
    {
        return string.Equals(Data?.TxnResponseCode, "INSUFFICIENT_FUNDS", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the failure reason is a card authentication failure (e.g., wrong OTP or 3-D Secure rejection).
    /// </summary>
    public bool IsAuthenticationFailedError()
    {
        return string.Equals(Data?.TxnResponseCode, "AUTHENTICATION_FAILED", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the issuing bank declined the transaction (generic decline; check <c>Data.Message</c> for the bank's reason).
    /// </summary>
    public bool IsDeclinedError()
    {
        // "data.message": may be "Do not honour", or "Invalid card number", ...
        return string.Equals(Data?.TxnResponseCode, "DECLINED", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <see langword="true"/> when Paymob's fraud management system rejected the transaction
    /// because it did not pass risk checks.
    /// </summary>
    public bool IsRiskChecksError()
    {
        return Data is { TxnResponseCode: "11", Message: not null }
            && Data.Message.Contains("transaction did not pass risk checks", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns normalised card information when the payment method was a card, or
    /// <see langword="null"/> for non-card channels.
    /// </summary>
    public CashInCardInfo? Card()
    {
        if (!IsCard())
        {
            return null;
        }

        var cardNumber = Data?.CardNum ?? SourceData?.Pan ?? "xxxx";

        var bank = _GetFromExtensions("card_holder_bank");
        bank = string.Equals(bank, "-", StringComparison.Ordinal) ? null : bank;

        var type = Data?.CardType ?? SourceData?.SubType ?? _GetFromExtensions("card_type");

        type = type?.ToUpperInvariant() switch
        {
            "MASTERCARD" => "MasterCard",
            "VISA" => "Visa",
            null => null,
            _ => type.Humanize(),
        };

        return new(cardNumber, type, bank);
    }

    private string? _GetFromExtensions(string name)
    {
        if (ExtensionData is null)
        {
            return null;
        }

        return ExtensionData.TryGetValue(name, out var value) ? ((JsonElement?)value)?.GetString() : null;
    }
}
