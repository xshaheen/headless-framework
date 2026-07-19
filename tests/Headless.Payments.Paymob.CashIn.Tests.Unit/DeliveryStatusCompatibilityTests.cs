// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.Services.CashIn.Models;

namespace Tests;

public sealed class DeliveryStatusCompatibilityTests
{
    [Fact]
    public void should_keep_delivery_status_numeric_contract_stable()
    {
        new[]
        {
            (int)DeliveryStatus.Scheduled,
            (int)DeliveryStatus.ContactingMerchant,
            (int)DeliveryStatus.PickingUp,
            (int)DeliveryStatus.CourierReceived,
            (int)DeliveryStatus.AtWarehouse,
            (int)DeliveryStatus.AgentOut,
            (int)DeliveryStatus.OnRoute,
            (int)DeliveryStatus.AtCustomer,
            (int)DeliveryStatus.Delivered,
            (int)DeliveryStatus.Canceled,
            (int)DeliveryStatus.DeliveryFailed,
            (int)DeliveryStatus.ReturnScheduled,
            (int)DeliveryStatus.PackageReturned,
        }
            .Should()
            .Equal(Enumerable.Range(0, 13));
    }
}
