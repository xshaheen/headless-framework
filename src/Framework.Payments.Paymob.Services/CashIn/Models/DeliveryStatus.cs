// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.Services.CashIn.Models;

public enum DeliveryStatus
{
    /// <summary>
    /// Successfully placed a new delivery on the courier’s system.
    /// </summary>
    Scheduled,

    /// <summary>
    /// The courier agent is in touch with your administration through provided contact
    /// info to pick up the package to be delivered.
    /// </summary>
    ContactingMerchant,

    /// <summary>
    /// Courier agent is headed towards your pick up address to receive the package.
    /// </summary>
    PickingUp,

    /// <summary>
    /// The courier agent picked up the package.
    /// </summary>
    CourierReceived,

    /// <summary>
    /// The package is placed at the courier’s storage, pending client’s scheduling
    /// </summary>
    AtWarehouse,

    /// <summary>
    /// Courier agent is out with your customer added on their list
    /// (not necessarily headed towards your client first).
    /// </summary>
    AgentOut,

    /// <summary>
    /// The courier agent is currently headed towards your client.
    /// </summary>
    OnRoute,

    /// <summary>
    /// The courier agent arrived at the customer’s address.
    /// </summary>
    AtCustomer,

    /// <summary>
    /// Courier completed their task successfully.
    /// </summary>
    Delivered,

    /// <summary>
    /// Customer / Merchant canceled courier’s delivery.
    /// </summary>
    Canceled,

    /// <summary>
    /// Courier agent was not able to fulfill their task. For example, the customer
    /// was not at home, the customer refused to pay or did not have enough money,
    /// package was malformed.
    /// </summary>
    DeliveryFailed,

    /// <summary>
    /// Courier is set to return the package back to you for exchange or a canceled delivery.
    /// </summary>
    ReturnScheduled,

    /// <summary>
    /// Courier returned the package to you successfully.
    /// </summary>
    PackageReturned,
}

internal static class DeliveryStatusExtensions
{
    internal static DeliveryStatus ToDeliveryStatus(this string status)
    {
        return status switch
        {
            "Scheduled" => DeliveryStatus.Scheduled,
            "Contacting Merchant" => DeliveryStatus.ContactingMerchant,
            "Picking Up" => DeliveryStatus.PickingUp,
            "Courier Received" => DeliveryStatus.CourierReceived,
            "At Warehouse" => DeliveryStatus.AtWarehouse,
            "Agent Out" => DeliveryStatus.AgentOut,
            "On Route" => DeliveryStatus.AgentOut,
            "At Customer" => DeliveryStatus.AgentOut,
            "Delivered" => DeliveryStatus.AgentOut,
            "Canceled" => DeliveryStatus.Canceled,
            "Delivery Failed" => DeliveryStatus.DeliveryFailed,
            "Return Scheduled" => DeliveryStatus.ReturnScheduled,
            "Package Returned" => DeliveryStatus.PackageReturned,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }
}
