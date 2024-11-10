// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using Framework.Kernel.Checks;

namespace Framework.Payments.Paymob.CashIn.Models.Orders;

[PublicAPI]
public sealed class CashInCreateOrderRequest
{
    private CashInCreateOrderRequest() { }

    public int AmountCents { get; private init; }

    public string Currency { get; private init; } = default!;

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
    public IEnumerable<CashInCreateOrderRequestOrderItem> Items { get; private init; } =
        Array.Empty<CashInCreateOrderRequestOrderItem>();

    /// <summary>Create order without delivery.</summary>
    public static CashInCreateOrderRequest CreateOrder(
        int amountCents,
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

    /// <summary>Create delivery order.</summary>
    public static CashInCreateOrderRequest CreateDeliveryOrder(
        CashInCreateOrderRequestShippingDetails shippingDetails,
        CashInCreateOrderRequestShippingData shippingData,
        ICollection<CashInCreateOrderRequestOrderItem> items,
        int amountCents,
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
