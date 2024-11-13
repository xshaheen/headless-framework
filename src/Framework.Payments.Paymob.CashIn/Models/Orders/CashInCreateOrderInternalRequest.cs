// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;

namespace Framework.Payments.Paymob.CashIn.Models.Orders;

[PublicAPI]
internal sealed class CashInCreateOrderInternalRequest(string authToken, CashInCreateOrderRequest request)
{
    [JsonPropertyName("auth_token")]
    public string AuthToken { get; } = authToken;

    [JsonPropertyName("amount_cents")]
    public int AmountCents { get; } = request.AmountCents;

    [JsonPropertyName("currency")]
    public string Currency { get; } = request.Currency;

    [JsonPropertyName("merchant_order_id")]
    public string? MerchantOrderId { get; } = request.MerchantOrderId;

    /// <summary>
    /// Set it to be true if your order needs to be delivered by Accept's product delivery services.
    /// </summary>
    [JsonPropertyName("delivery_needed")]
    public string DeliveryNeeded { get; } = request.DeliveryNeeded;

    /// <summary>Mandatory if your order needs to be delivered, otherwise you can delete the whole object.</summary>
    [JsonPropertyName("shipping_data")]
    public CashInCreateOrderRequestShippingData? ShippingData { get; } = request.ShippingData;

    /// <summary>Mandatory if your order needs to be delivered, otherwise you can delete the whole object.</summary>
    [JsonPropertyName("shipping_details")]
    public CashInCreateOrderRequestShippingDetails? ShippingDetails { get; } = request.ShippingDetails;

    /// <summary>The list of objects contains the contents of the order if it is existing.</summary>
    [JsonPropertyName("items")]
    public IEnumerable<CashInCreateOrderRequestOrderItem> Items { get; } = request.Items;
}
