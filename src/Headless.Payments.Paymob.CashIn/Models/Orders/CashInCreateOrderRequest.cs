// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Payments.Paymob.CashIn.Models.Orders;

/// <summary>
/// Represents the request body for creating a Paymob Accept order. Use the static factory
/// methods to construct instances.
/// </summary>
/// <remarks>
/// An order is a logical container required before obtaining a payment key. Use
/// <c>CreateOrder</c> for standard orders and <c>CreateDeliveryOrder</c> when Paymob's
/// courier delivery service is needed.
/// </remarks>
[PublicAPI]
public sealed class CashInCreateOrderRequest
{
    private CashInCreateOrderRequest() { }

    /// <summary>The order amount in the smallest currency unit (integer cents), e.g. <c>10000</c> for 100.00 EGP.</summary>
    public long AmountCents { get; private init; }

    /// <summary>ISO 4217 currency code for the order (for example <c>EGP</c>).</summary>
    public string Currency { get; private init; } = null!;

    /// <summary>Your own order reference ID, used to correlate Paymob orders with your system. Optional.</summary>
    public string? MerchantOrderId { get; private init; }

    /// <summary>
    /// Set it to be true if your order needs to be delivered by Accept's product delivery services.
    /// </summary>
    public string DeliveryNeeded { get; private init; } = "false";

    /// <summary>
    /// Mandtaory if your order needs to be delivered, otherwise you can delete the whole object.
    /// </summary>
    public CashInCreateOrderRequestShippingData? ShippingData { get; private init; }

    /// <summary>
    /// Mandatory if your order needs to be delivered, otherwise you can delete the whole object.
    /// </summary>
    public CashInCreateOrderRequestShippingDetails? ShippingDetails { get; private init; }

    /// <summary>
    /// The list of objects contains the contents of the order if it is existing.
    /// </summary>
    public IEnumerable<CashInCreateOrderRequestOrderItem> Items { get; private init; } = [];

    /// <summary>Creates a standard order without delivery.</summary>
    /// <param name="amountCents">The order amount in the smallest currency unit (cents). Must be positive.</param>
    /// <param name="currency">ISO 4217 currency code. Defaults to <c>EGP</c>.</param>
    /// <param name="merchantOrderId">Your own order reference ID, used to correlate Paymob orders with your system.</param>
    /// <returns>A configured <c>CashInCreateOrderRequest</c> with <c>DeliveryNeeded</c> set to false.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amountCents"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="currency"/> is null or empty.</exception>
    public static CashInCreateOrderRequest CreateOrder(
        long amountCents,
        string currency = "EGP",
        string? merchantOrderId = null
    )
    {
        Argument.IsPositive(amountCents);
        Argument.IsNotNullOrEmpty(currency);

        return new()
        {
            AmountCents = amountCents,
            DeliveryNeeded = "false",
            Currency = currency,
            MerchantOrderId = merchantOrderId,
        };
    }

    /// <summary>Creates an order that will be fulfilled via Paymob's courier delivery service.</summary>
    /// <param name="shippingDetails">Dimensions and weight of the parcel to be delivered.</param>
    /// <param name="shippingData">Recipient contact and address information.</param>
    /// <param name="items">The line items in the order. Must not be empty.</param>
    /// <param name="amountCents">The order amount in the smallest currency unit (cents). Must be positive.</param>
    /// <param name="currency">ISO 4217 currency code. Defaults to <c>EGP</c>.</param>
    /// <param name="merchantOrderId">Your own order reference ID.</param>
    /// <returns>A configured <c>CashInCreateOrderRequest</c> with <c>DeliveryNeeded</c> set to true.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amountCents"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="currency"/> is null or empty; or <paramref name="items"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="shippingDetails"/> or <paramref name="shippingData"/> is <see langword="null"/>.</exception>
    public static CashInCreateOrderRequest CreateDeliveryOrder(
        CashInCreateOrderRequestShippingDetails shippingDetails,
        CashInCreateOrderRequestShippingData shippingData,
        ICollection<CashInCreateOrderRequestOrderItem> items,
        long amountCents,
        string currency = "EGP",
        string? merchantOrderId = null
    )
    {
        Argument.IsPositive(amountCents);
        Argument.IsNotNullOrEmpty(currency);
        Argument.IsNotNull(shippingDetails);
        Argument.IsNotNull(shippingData);
        Argument.IsNotNullOrEmpty(items);

        return new()
        {
            AmountCents = amountCents,
            DeliveryNeeded = "true",
            Currency = currency,
            MerchantOrderId = merchantOrderId,
            ShippingDetails = shippingDetails,
            ShippingData = shippingData,
            Items = items,
        };
    }
}
