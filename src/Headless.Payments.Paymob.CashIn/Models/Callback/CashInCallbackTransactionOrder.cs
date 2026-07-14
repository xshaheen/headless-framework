// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Internals;
using Headless.Payments.Paymob.CashIn.Models.Orders;

namespace Headless.Payments.Paymob.CashIn.Models.Callback;

[PublicAPI]
public sealed class CashInCallbackTransactionOrder
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("created_at")]
    [JsonConverter(typeof(AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter))]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("delivery_needed")]
    public bool DeliveryNeeded { get; init; }

    [JsonPropertyName("amount_cents")]
    public long AmountCents { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("is_payment_locked")]
    public bool IsPaymentLocked { get; init; }

    [JsonPropertyName("is_return")]
    public bool IsReturn { get; init; }

    [JsonPropertyName("is_cancel")]
    public bool IsCancel { get; init; }

    [JsonPropertyName("is_returned")]
    public bool IsReturned { get; init; }

    [JsonPropertyName("is_canceled")]
    public bool IsCanceled { get; init; }

    [JsonPropertyName("paid_amount_cents")]
    public long PaidAmountCents { get; init; }

    [JsonPropertyName("notify_user_with_email")]
    public bool NotifyUserWithEmail { get; init; }

    [JsonPropertyName("order_url")]
    public string? OrderUrl { get; init; }

    [JsonPropertyName("commission_fees")]
    public long CommissionFees { get; init; }

    [JsonPropertyName("delivery_fees_cents")]
    public long DeliveryFeesCents { get; init; }

    [JsonPropertyName("delivery_vat_cents")]
    public long DeliveryVatCents { get; init; }

    [JsonPropertyName("payment_method")]
    public string? PaymentMethod { get; init; }

    [JsonPropertyName("api_source")]
    public string? ApiSource { get; init; }

    [JsonPropertyName("merchant")]
    public CashInOrderMerchant? Merchant { get; init; }

    [JsonPropertyName("shipping_data")]
    public CashInOrderShippingData? ShippingData { get; init; }

    [JsonPropertyName("shipping_details")]
    public CashInCallbackTransactionOrderShippingDetails? ShippingDetails { get; init; }

    [JsonPropertyName("collector")]
    public CashInCallbackTransactionOrderCollector? Collector { get; init; }

    /// <summary>Opaque Paymob passthrough value; shape is provider-defined and usually <see langword="null"/>.</summary>
    [JsonPropertyName("data")]
    public object? Data { get; init; }

    /// <summary>Opaque Paymob passthrough value; shape is provider-defined and usually <see langword="null"/>.</summary>
    [JsonPropertyName("merchant_staff_tag")]
    public object? MerchantStaffTag { get; init; }

    /// <summary>Opaque Paymob passthrough value; shape is provider-defined and usually <see langword="null"/>.</summary>
    [JsonPropertyName("pickup_data")]
    public object? PickupData { get; init; }

    /// <summary>Your own order reference ID echoed back by Paymob, when one was supplied on order creation.</summary>
    [JsonPropertyName("merchant_order_id")]
    public string? MerchantOrderId { get; init; }

    /// <summary>Opaque Paymob passthrough value; shape is provider-defined and usually <see langword="null"/>.</summary>
    [JsonPropertyName("wallet_notification")]
    public object? WalletNotification { get; init; }

    /// <summary>Order line items as returned by Paymob; elements are opaque provider-defined objects. Never <see langword="null"/>.</summary>
    [JsonPropertyName("items")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<object?> Items
    {
        get => field ?? [];
        init;
    }

    /// <summary>Delivery status entries as returned by Paymob; elements are opaque provider-defined objects. Never <see langword="null"/>.</summary>
    [JsonPropertyName("delivery_status")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<object?> DeliveryStatus
    {
        get => field ?? [];
        init;
    }

    /// <summary>Unmodelled JSON fields returned by Paymob, captured so no callback data is lost.</summary>
    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
