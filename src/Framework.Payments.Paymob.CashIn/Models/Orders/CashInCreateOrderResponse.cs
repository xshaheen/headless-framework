// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Payments.Paymob.CashIn.Internals;

namespace Framework.Payments.Paymob.CashIn.Models.Orders;

[PublicAPI]
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class CashInCreateOrderResponse
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("merchant_order_id")]
    public string? MerchantOrderId { get; init; }

    [JsonPropertyName("created_at")]
    [JsonConverter(typeof(AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter))]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("amount_cents")]
    public int AmountCents { get; init; }

    [JsonPropertyName("paid_amount_cents")]
    public int PaidAmountCents { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("commission_fees")]
    public int CommissionFees { get; init; }

    [JsonPropertyName("delivery_fees_cents")]
    public int DeliveryFeesCents { get; init; }

    [JsonPropertyName("delivery_vat_cents")]
    public int DeliveryVatCents { get; init; }

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

    [JsonPropertyName("delivery_needed")]
    public bool DeliveryNeeded { get; init; }

    [JsonPropertyName("notify_user_with_email")]
    public bool NotifyUserWithEmail { get; init; }

    [JsonPropertyName("payment_method")]
    public required string PaymentMethod { get; init; }

    [JsonPropertyName("api_source")]
    public required string ApiSource { get; init; }

    [JsonPropertyName("token")]
    public string? Token { get; init; }

    [JsonPropertyName("order_url")]
    public string? OrderUrl { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("merchant")]
    public required CashInOrderMerchant Merchant { get; init; }

    [JsonPropertyName("shipping_data")]
    public CashInOrderShippingData? ShippingData { get; init; }

    [JsonPropertyName("shipping_details")]
    public CashInOrderShippingDetails? ShippingDetails { get; init; }

    [JsonPropertyName("items")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<CashInOrderItem> Items
    {
        get => field ?? [];
        init;
    }

    [JsonPropertyName("delivery_status")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<object?> DeliveryStatus
    {
        get => field ?? [];
        init;
    }

    [JsonPropertyName("wallet_notification")]
    public object? WalletNotification { get; init; }

    [JsonPropertyName("merchant_staff_tag")]
    public object? MerchantStaffTag { get; init; }

    [JsonPropertyName("pickup_data")]
    public object? PickupData { get; init; }

    [JsonPropertyName("collector")]
    public object? Collector { get; init; }

    [JsonPropertyName("data")]
    public object? Data { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
